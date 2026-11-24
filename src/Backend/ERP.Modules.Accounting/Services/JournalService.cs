using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Accounting.Data;
using ERP.Modules.Accounting.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Accounting.Services;

/// <summary>
/// Libro diario de la empresa activa (ACC-03). Solo lectura: los asientos no se editan ni se borran.
/// El filtro de fechas usa la fecha contable, no el instante UTC de registro.
/// </summary>
public sealed class JournalService(AccountingDbContext db)
{
    public Task<PagedResult<JournalEntryResponse>> ListAsync(
        PageRequest page, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            if (from is { } start && to is { } end && start > end)
            {
                throw new DomainException("journal_date_range_invalid", "La fecha inicial no puede ser posterior a la final.");
            }

            var query = db.JournalEntries.AsNoTracking();
            if (from is { } since)
            {
                query = query.Where(e => e.AccountingDate >= since);
            }

            if (to is { } until)
            {
                query = query.Where(e => e.AccountingDate <= until);
            }

            var count = await query.CountAsync(ct);
            var entries = await query
                .OrderByDescending(e => e.AccountingDate).ThenByDescending(e => e.PostedAtUtc).ThenBy(e => e.Id)
                .Skip(page.Skip).Take(page.SafePageSize)
                .ToListAsync(ct);

            var ids = entries.Select(e => e.Id).ToArray();
            var lines = await LinesAsync(ids, ct);
            var items = entries
                .Select(e => ToResponse(e, lines.TryGetValue(e.Id, out var own) ? own : []))
                .ToList();
            return new PagedResult<JournalEntryResponse>(items, count, page.SafePage, page.SafePageSize);
        }, cancellationToken);

    public Task<JournalEntryResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var entry = await db.JournalEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct)
                ?? throw new NotFoundException("Asiento", id);
            var lines = await LinesAsync([id], ct);
            return ToResponse(entry, lines.TryGetValue(id, out var own) ? own : []);
        }, cancellationToken);

    /// <summary>
    /// Líneas de los asientos indicados, con el nombre actual de la cuenta. La unión con el plan es
    /// interna porque la clave foránea garantiza que la cuenta existe en la misma empresa.
    /// </summary>
    private async Task<Dictionary<Guid, List<JournalLineResponse>>> LinesAsync(Guid[] entryIds, CancellationToken ct)
    {
        if (entryIds.Length == 0)
        {
            return [];
        }

        var rows = await db.JournalLines.AsNoTracking()
            .Where(l => entryIds.Contains(l.JournalEntryId))
            .Join(db.Accounts.AsNoTracking(), l => l.AccountId, a => a.Id, (l, a) => new
            {
                l.JournalEntryId,
                Line = new JournalLineResponse(l.LineNumber, l.AccountId, l.AccountCode, a.Name, l.Debit, l.Credit, l.Memo),
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.JournalEntryId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Line).OrderBy(l => l.LineNumber).ToList());
    }

    private static JournalEntryResponse ToResponse(Models.JournalEntryEntity entry, IReadOnlyList<JournalLineResponse> lines) =>
        new(entry.Id, entry.AccountingDate, entry.Description, entry.Status.ToString(),
            entry.SourceModule, entry.SourceDocumentType, entry.SourceDocumentId, entry.PostedAtUtc,
            lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), lines);
}
