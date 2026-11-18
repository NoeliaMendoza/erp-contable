namespace ERP.BuildingBlocks.Paging;

/// <summary>
/// Parámetros comunes de listado (contrato CRUD v1): <c>page</c> desde 1, <c>pageSize</c> 20 por defecto
/// y máximo 100, <c>search</c> opcional. Los valores fuera de rango se ajustan en lugar de fallar.
/// </summary>
public sealed record PageRequest(int Page = 1, int PageSize = PageRequest.DefaultPageSize, string? Search = null)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int SafePage => Page < 1 ? 1 : Page;

    public int SafePageSize => PageSize switch
    {
        < 1 => DefaultPageSize,
        > MaxPageSize => MaxPageSize,
        _ => PageSize,
    };

    public int Skip => (SafePage - 1) * SafePageSize;

    /// <summary>Texto de búsqueda recortado, o null si está vacío.</summary>
    public string? Term => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}

/// <summary>Respuesta paginada común (JSON camelCase: items, totalCount, page, pageSize).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
