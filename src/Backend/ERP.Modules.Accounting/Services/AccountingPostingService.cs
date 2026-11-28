using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Accounting.Contracts;
using ERP.Modules.Accounting.Data;
using ERP.Modules.Accounting.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Modules.Accounting.Services;

/// <summary>
/// Único camino para que otro módulo genere asientos (ACUERDO-02, ACC-03).
/// Garantiza partida doble, cuentas válidas de la empresa activa, trazabilidad al documento origen
/// e idempotencia por <c>(tenant_id, idempotency_key)</c>. No hace commit propio: participa en la
/// transacción del caso de uso que la invoca y propaga las excepciones para que se revierta todo.
/// </summary>
/// <remarks>
/// El período contable todavía no tiene catálogo ni política acordada, así que <c>period_closed</c>
/// no se evalúa aquí; se incorporará cuando exista ese acuerdo (ver docs/contratos-modulos.md).
/// </remarks>
public sealed class AccountingPostingService(AccountingDbContext db, ITenantContext tenant, TimeProvider clock,
    SharedTenantTransactionContext sharedTransaction)
    : IAccountingPostingService
{
    private const string Savepoint = "accounting_posting";
    private const string IdempotencyIndex = "ix_journal_entries_tenant_id_idempotency_key";

    /// <summary>Línea ya validada y con el código de cuenta normalizado.</summary>
    private sealed record NormalizedLine(string AccountCode, decimal Debit, decimal Credit, string? Memo);

    /// <summary>Asiento existente con sus líneas, para comparar el contenido de un reintento.</summary>
    private sealed record StoredEntry(JournalEntryEntity Entry, IReadOnlyList<NormalizedLine> Lines);

    public Task<PostingResult> PostAsync(PostingRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(sharedTransaction, ct => PostCoreAsync(request, ct), cancellationToken);

    /// <summary>
    /// Un asiento contabilizado no se elimina ni se edita. El reverso auditable queda pendiente del
    /// acuerdo sobre la corrección del documento y sus efectos en inventario (ACUERDO-02).
    /// </summary>
    public Task<PostingResult> ReverseAsync(Guid journalEntryId, DateOnly reversalDate, string reason, string idempotencyKey, CancellationToken cancellationToken)
        => throw new DomainException("journal_reverse_pending", "El reverso de asientos no está habilitado todavía.");

    private async Task<PostingResult> PostCoreAsync(PostingRequest request, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var lines = Normalize(request);

        // El reintento se resuelve antes de validar cuentas o período: un asiento ya confirmado se
        // devuelve aunque después se desactive una cuenta, pero eso no autoriza un asiento nuevo.
        if (await FindByKeyAsync(request.IdempotencyKey, ct) is { } known)
        {
            return Reconcile(known, request, lines);
        }

        var accounts = await ResolveAccountsAsync(lines, ct);
        var entry = JournalEntryEntity.Create(tenantId, Guid.CreateVersion7(), request.AccountingDate,
            request.Description.Trim(), request.IdempotencyKey.Trim(), request.SourceModule.Trim(),
            request.SourceDocumentType.Trim(), request.SourceDocumentId, clock.GetUtcNow());

        db.JournalEntries.Add(entry);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            db.JournalLines.Add(JournalEntryLineEntity.Create(tenantId, entry.Id, index + 1,
                accounts[line.AccountCode], line.AccountCode, line.Debit, line.Credit, line.Memo));
        }

        return await SaveAsync(entry, request, lines, ct);
    }

    /// <summary>
    /// Guarda dentro de un savepoint: si otra solicitud simultánea gana la restricción única, la
    /// colisión no deja la transacción abortada y el reintento se resuelve leyendo el asiento ganador.
    /// </summary>
    private async Task<PostingResult> SaveAsync(JournalEntryEntity entry, PostingRequest request,
        IReadOnlyList<NormalizedLine> lines, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("La contabilización requiere una transacción activa para el contexto de empresa.");

        await transaction.CreateSavepointAsync(Savepoint, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.ReleaseSavepointAsync(Savepoint, ct);
            return new PostingResult(entry.Id, false);
        }
        catch (DbUpdateException ex) when (IsIdempotencyCollision(ex))
        {
            await transaction.RollbackToSavepointAsync(Savepoint, ct);
            Forget(entry);

            var winner = await FindByKeyAsync(request.IdempotencyKey, ct)
                ?? throw new ConflictException("posting_idempotency_conflict",
                    "La clave de idempotencia ya está en uso en esta empresa.");
            return Reconcile(winner, request, lines);
        }
    }

    private static bool IsIdempotencyCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IdempotencyIndex,
        };

    /// <summary>Descarta el asiento perdedor del rastreador para no reintentar su inserción.</summary>
    private void Forget(JournalEntryEntity entry)
    {
        foreach (var tracked in db.ChangeTracker.Entries<JournalEntryLineEntity>()
            .Where(e => e.Entity.JournalEntryId == entry.Id).ToList())
        {
            tracked.State = EntityState.Detached;
        }

        db.Entry(entry).State = EntityState.Detached;
    }

    private async Task<StoredEntry?> FindByKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        var key = idempotencyKey.Trim();
        var entry = await db.JournalEntries.AsNoTracking().SingleOrDefaultAsync(e => e.IdempotencyKey == key, ct);
        if (entry is null)
        {
            return null;
        }

        var lines = await db.JournalLines.AsNoTracking()
            .Where(l => l.JournalEntryId == entry.Id)
            .OrderBy(l => l.LineNumber)
            .Select(l => new NormalizedLine(l.AccountCode, l.Debit, l.Credit, l.Memo))
            .ToListAsync(ct);
        return new StoredEntry(entry, lines);
    }

    /// <summary>
    /// Misma clave y mismo contenido devuelve el asiento original sin crear otro; misma clave con
    /// contenido distinto es conflicto 409 y nunca sobrescribe lo ya contabilizado.
    /// </summary>
    private static PostingResult Reconcile(StoredEntry stored, PostingRequest request, IReadOnlyList<NormalizedLine> lines)
    {
        var sameHeader = string.Equals(stored.Entry.SourceModule, request.SourceModule.Trim(), StringComparison.Ordinal)
            && string.Equals(stored.Entry.SourceDocumentType, request.SourceDocumentType.Trim(), StringComparison.Ordinal)
            && stored.Entry.SourceDocumentId == request.SourceDocumentId
            && stored.Entry.AccountingDate == request.AccountingDate
            && string.Equals(stored.Entry.Description, request.Description.Trim(), StringComparison.Ordinal);

        if (sameHeader && stored.Lines.SequenceEqual(lines))
        {
            return new PostingResult(stored.Entry.Id, true);
        }

        throw new ConflictException("posting_idempotency_conflict",
            $"La clave '{request.IdempotencyKey.Trim()}' ya identifica un asiento con otro contenido en esta empresa.");
    }

    /// <summary>
    /// Resuelve cada código contra el plan de la empresa activa. Debe existir, estar activa y admitir
    /// movimientos; cualquier otro caso se rechaza sin revelar datos de otra empresa.
    /// </summary>
    private async Task<Dictionary<string, Guid>> ResolveAccountsAsync(IReadOnlyList<NormalizedLine> lines, CancellationToken ct)
    {
        var codes = lines.Select(l => l.AccountCode).Distinct(StringComparer.Ordinal).ToArray();
        var usable = await db.Accounts.AsNoTracking()
            .Where(a => codes.Contains(a.Code) && a.IsActive && a.AllowsPosting)
            .Select(a => new { a.Code, a.Id })
            .ToDictionaryAsync(a => a.Code, a => a.Id, StringComparer.Ordinal, ct);

        var unavailable = codes.Where(code => !usable.ContainsKey(code)).ToArray();
        return unavailable.Length == 0
            ? usable
            : throw new DomainException("posting_account_not_available",
                $"Estas cuentas no existen en el plan de la empresa activa, están inactivas o no admiten movimientos: {string.Join(", ", unavailable)}.");
    }

    private static List<NormalizedLine> Normalize(PostingRequest request)
    {
        if (IsBlankOrTooLong(request.IdempotencyKey, JournalEntryEntity.IdempotencyKeyMaxLength)
            || IsBlankOrTooLong(request.SourceModule, JournalEntryEntity.SourceMaxLength)
            || IsBlankOrTooLong(request.SourceDocumentType, JournalEntryEntity.SourceMaxLength)
            || request.SourceDocumentId == Guid.Empty)
        {
            throw new DomainException("posting_source_invalid",
                "La contabilización requiere clave de idempotencia, módulo, tipo y documento de origen válidos.");
        }

        if (IsBlankOrTooLong(request.Description, JournalEntryEntity.DescriptionMaxLength))
        {
            throw new DomainException("journal_description_required",
                $"El asiento requiere una descripción de 1 a {JournalEntryEntity.DescriptionMaxLength} caracteres.");
        }

        if (request.Lines is not { Count: >= 2 })
        {
            throw new DomainException("journal_min_lines", "Un asiento requiere al menos dos líneas.");
        }

        var lines = new List<NormalizedLine>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            if (line.Debit < 0 || line.Credit < 0)
            {
                throw new DomainException("journal_line_negative", "Debe y haber no pueden ser negativos.");
            }

            if ((line.Debit > 0) == (line.Credit > 0))
            {
                throw new DomainException("journal_line_side",
                    "Cada línea debe tener importe en debe o en haber, no en ambos ni en ninguno.");
            }

            if (!AccountingMoney.HasValidScale(line.Debit) || !AccountingMoney.HasValidScale(line.Credit))
            {
                throw new DomainException("journal_line_scale",
                    $"Los importes admiten como máximo {AccountingMoney.Scale} decimales.");
            }

            lines.Add(new NormalizedLine(NormalizeCode(line.AccountCode), line.Debit, line.Credit, Clean(line.Memo)));
        }

        var debit = lines.Sum(l => l.Debit);
        var credit = lines.Sum(l => l.Credit);
        if (debit != credit || debit <= 0)
        {
            throw new DomainException("journal_unbalanced",
                $"Partida doble incumplida: debe {debit:0.00} ≠ haber {credit:0.00}.");
        }

        return lines;
    }

    /// <summary>
    /// Normaliza el código igual que ACC-01. Un código con formato imposible en el plan (por ejemplo
    /// un nombre de cuenta con espacios) se trata como cuenta no disponible, sin revelar el plan.
    /// </summary>
    private static string NormalizeCode(string accountCode)
    {
        try
        {
            return Account.NormalizeCode(accountCode);
        }
        catch (DomainException)
        {
            throw new DomainException("posting_account_not_available",
                $"'{accountCode}' no es un código de cuenta del plan de la empresa activa.");
        }
    }

    private static string? Clean(string? memo) => string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();

    private static bool IsBlankOrTooLong(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength;
}
