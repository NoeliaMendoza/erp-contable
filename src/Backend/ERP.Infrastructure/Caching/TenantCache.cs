using System.Text.Json;
using ERP.BuildingBlocks.Caching;
using ERP.BuildingBlocks.Tenancy;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Caching;

/// <summary>
/// Caché distribuida (Redis o memoria) con claves <c>erp:t:{tenantId}:{clave}</c>.
/// Sin tenant activo lanza: no existen claves "globales" accidentales.
/// Errores de Redis se registran y se tratan como fallo de caché (la BD sigue siendo la fuente de verdad).
/// </summary>
public sealed class TenantCache(IDistributedCache cache, ITenantContext tenantContext, ILogger<TenantCache> logger)
    : ITenantCache
{
    public static string BuildKey(Guid tenantId, string key) => $"erp:t:{tenantId:N}:{key}";

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = BuildKey(tenantContext.RequireTenantId(), key);
        try
        {
            var bytes = await cache.GetAsync(fullKey, cancellationToken);
            return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Fallo de caché al leer; se continúa sin caché");
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var fullKey = BuildKey(tenantContext.RequireTenantId(), key);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            await cache.SetAsync(fullKey, bytes, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Fallo de caché al escribir; se ignora");
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = BuildKey(tenantContext.RequireTenantId(), key);
        try
        {
            await cache.RemoveAsync(fullKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Fallo de caché al invalidar; se ignora");
        }
    }
}
