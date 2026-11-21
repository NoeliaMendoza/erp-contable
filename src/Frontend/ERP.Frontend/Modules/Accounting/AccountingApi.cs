using ERP.Frontend.Shared.Api;

namespace ERP.Frontend.Modules.Accounting;

// Espejo del contrato HTTP; no referencia la implementación del backend.
public sealed record AccountItem(Guid Id, string Code, string Name, string Kind, string NormalBalance, Guid? ParentId, bool AllowsPosting, bool IsActive)
{
    public string Display => $"{Code} - {Name}";

    public string Status => IsActive ? "Activa" : "Inactiva";

    public string KindLabel => AccountOption.Kinds.FirstOrDefault(o => o.Value == Kind)?.Label ?? Kind;

    public string BalanceLabel => AccountOption.Balances.FirstOrDefault(o => o.Value == NormalBalance)?.Label ?? NormalBalance;

    public string PostingLabel => AllowsPosting ? "Sí" : "No";
}

public sealed record SaveAccount(string Code, string Name, string Kind, string NormalBalance, Guid? ParentId, bool AllowsPosting);

public sealed record ParentAccountOption(Guid? Id, string Display);

public sealed record AccountOption(string Value, string Label)
{
    public static readonly IReadOnlyList<AccountOption> Kinds =
    [
        new("Asset", "Activo"),
        new("Liability", "Pasivo"),
        new("Equity", "Patrimonio"),
        new("Income", "Ingreso"),
        new("Expense", "Gasto"),
    ];

    public static readonly IReadOnlyList<AccountOption> Balances =
    [
        new("Debit", "Deudora"),
        new("Credit", "Acreedora"),
    ];
}

public sealed class AccountingApi(ErpApiClient api)
{
    public Task<PagedResult<AccountItem>> ListAsync(int page, int pageSize, string? search, bool? isActive, CancellationToken ct = default)
    {
        var query = $"page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += "&search=" + Uri.EscapeDataString(search.Trim());
        }

        if (isActive is not null)
        {
            query += "&isActive=" + (isActive.Value ? "true" : "false");
        }

        return api.GetAsync<PagedResult<AccountItem>>("api/v1/accounts?" + query, ct);
    }

    public Task<AccountItem> GetAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<AccountItem>($"api/v1/accounts/{id}", ct);

    public Task<AccountItem> CreateAsync(SaveAccount request, CancellationToken ct = default) =>
        api.SendJsonAsync<AccountItem>(HttpMethod.Post, "api/v1/accounts", request, ct);

    public Task<AccountItem> UpdateAsync(Guid id, SaveAccount request, CancellationToken ct = default) =>
        api.SendJsonAsync<AccountItem>(HttpMethod.Put, $"api/v1/accounts/{id}", request, ct);

    public Task<AccountItem> SetStatusAsync(Guid id, bool isActive, CancellationToken ct = default) =>
        api.SendJsonAsync<AccountItem>(HttpMethod.Patch, $"api/v1/accounts/{id}/status", new { isActive }, ct);
}
