using System.Globalization;
using ERP.Frontend.Shared.Api;

namespace ERP.Frontend.Modules.Accounting;

// Espejo del contrato HTTP del libro diario; no referencia la implementación del backend.

public sealed record JournalLineItem(
    int LineNumber, Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit, string? Memo)
{
    public string Account => $"{AccountCode} - {AccountName}";

    public string DebitLabel => Amount(Debit);

    public string CreditLabel => Amount(Credit);

    public string MemoLabel => Memo ?? string.Empty;

    private static string Amount(decimal value) =>
        value == 0 ? string.Empty : value.ToString("N2", CultureInfo.CurrentCulture);
}

public sealed record JournalEntryItem(
    Guid Id, DateOnly AccountingDate, string Description, string Status,
    string SourceModule, string SourceDocumentType, Guid SourceDocumentId, DateTimeOffset PostedAtUtc,
    decimal TotalDebit, decimal TotalCredit, IReadOnlyList<JournalLineItem> Lines)
{
    public string DateLabel => AccountingDate.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);

    public string SourceLabel => $"{SourceModule} · {SourceDocumentType}";

    public string TotalLabel => TotalDebit.ToString("N2", CultureInfo.CurrentCulture);

    public string StatusLabel => Status switch
    {
        "Posted" => "Contabilizado",
        "Reversed" => "Reversado",
        "Draft" => "Borrador",
        _ => Status,
    };
}

public sealed class JournalApi(ErpApiClient api)
{
    public Task<PagedResult<JournalEntryItem>> ListAsync(
        int page, int pageSize, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var query = $"page={page}&pageSize={pageSize}";
        if (from is { } since)
        {
            query += "&from=" + since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (to is { } until)
        {
            query += "&to=" + until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return api.GetAsync<PagedResult<JournalEntryItem>>("api/v1/journal-entries?" + query, ct);
    }
}
