namespace ERP.BuildingBlocks.Caching;

/// <summary>
/// Caché auxiliar segregada por tenant. PostgreSQL es la fuente de verdad: si la caché
/// falla, las implementaciones deben degradar a "sin caché" y nunca afectar la integridad.
/// </summary>
public interface ITenantCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
