using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Caching;
using ERP.Infrastructure.Tenancy;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ERP.UnitTests.Infrastructure;

public sealed class TenantCacheTests
{
    private readonly MemoryDistributedCache _shared = new(Options.Create(new MemoryDistributedCacheOptions()));

    private TenantCache For(Guid tenantId)
    {
        var session = TenantSession.ForSystem(tenantId);
        return new TenantCache(_shared, session, NullLogger<TenantCache>.Instance);
    }

    [Fact]
    public async Task La_misma_clave_no_se_comparte_entre_tenants()
    {
        var a = For(Guid.NewGuid());
        var b = For(Guid.NewGuid());

        await a.SetAsync("dashboard", "datos-A", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.Equal("datos-A", await a.GetAsync<string>("dashboard", TestContext.Current.CancellationToken));
        Assert.Null(await b.GetAsync<string>("dashboard", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sin_tenant_activo_no_hay_acceso_a_cache()
    {
        var cache = new TenantCache(_shared, TenantSession.ForSystem(), NullLogger<TenantCache>.Instance);

        await Assert.ThrowsAsync<TenantContextMissingException>(() => cache.GetAsync<string>("x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Un_fallo_de_cache_no_rompe_la_operacion()
    {
        var cache = new TenantCache(new FailingCache(), TenantSession.ForSystem(Guid.NewGuid()), NullLogger<TenantCache>.Instance);

        Assert.Null(await cache.GetAsync<string>("x", TestContext.Current.CancellationToken));
        await cache.SetAsync("x", "y", TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void La_clave_incluye_el_tenant()
    {
        var tenant = Guid.Parse("0195a000-0000-7000-8000-00000000a001");
        Assert.Equal("erp:t:0195a00000007000800000000000a001:k", TenantCache.BuildKey(tenant, "k"));
    }

    private sealed class FailingCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("redis caído");

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("redis caído");

        public void Refresh(string key) => throw new InvalidOperationException();

        public Task RefreshAsync(string key, CancellationToken token = default) => throw new InvalidOperationException();

        public void Remove(string key) => throw new InvalidOperationException();

        public Task RemoveAsync(string key, CancellationToken token = default) => throw new InvalidOperationException();

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw new InvalidOperationException("redis caído");
    }
}
