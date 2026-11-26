using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Accounting.Contracts;
using ERP.Modules.Accounting.Data;
using ERP.Modules.Accounting.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Accounting.Services;

public sealed class AccountingPostingService(AccountingDbContext db, ITenantContext tenant, TimeProvider clock,
    SharedTenantTransactionContext sharedTransaction)
    : IAccountingPostingService
{
    public Task<PostingResult> PostAsync(PostingRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(sharedTransaction, async ct =>
        {
            var tenantId = tenant.RequireTenantId();
            var existing = await db.JournalEntries.AsNoTracking()
                .SingleOrDefaultAsync(e => e.IdempotencyKey == request.IdempotencyKey, ct);
            if (existing is not null)
                return new PostingResult(existing.Id, true);

            if (request.Lines.Count < 2)
                throw new DomainException("journal_min_lines", "Un asiento requiere al menos dos líneas.");
            if (request.Lines.Any(l => (l.Debit > 0) == (l.Credit > 0)
                                      || l.Debit < 0 || l.Credit < 0
                                      || decimal.Round(l.Debit, 2) != l.Debit
                                      || decimal.Round(l.Credit, 2) != l.Credit))
                throw new DomainException("journal_line_invalid", "Las líneas contables son inválidas.");

            var debit = request.Lines.Sum(l => l.Debit);
            var credit = request.Lines.Sum(l => l.Credit);
            if (debit != credit || debit <= 0)
                throw new DomainException("journal_unbalanced", $"Partida doble incumplida: debe {debit:0.00} ≠ haber {credit:0.00}.");

            if (request is { SourceModule: "purchases", SourceDocumentType: "receipt" })
            {
                var codes = request.Lines.Select(line => Account.NormalizeCode(line.AccountCode)).Distinct().ToArray();
                var available = await db.Accounts.CountAsync(a => codes.Contains(a.Code) && a.IsActive && a.AllowsPosting, ct);
                if (available != codes.Length)
                    throw new DomainException("journal_account_unavailable", "Las cuentas de la recepción deben estar activas y permitir movimientos en la empresa.");
            }

            var entry = JournalEntryEntity.Create(tenantId, Guid.CreateVersion7(), request.AccountingDate, request.Description,
                request.IdempotencyKey, request.SourceModule, request.SourceDocumentType, request.SourceDocumentId, clock.GetUtcNow());
            db.JournalEntries.Add(entry);
            foreach (var line in request.Lines)
                db.JournalLines.Add(JournalEntryLineEntity.Create(tenantId, entry.Id, line.AccountCode, line.Debit, line.Credit, line.Memo));
            await db.SaveChangesAsync(ct);
            return new PostingResult(entry.Id, false);
        }, cancellationToken);

    public Task<PostingResult> ReverseAsync(Guid journalEntryId, DateOnly reversalDate, string reason, string idempotencyKey, CancellationToken cancellationToken)
        => throw new DomainException("journal_reverse_pending", "El reverso de asientos no está habilitado todavía.");
}
