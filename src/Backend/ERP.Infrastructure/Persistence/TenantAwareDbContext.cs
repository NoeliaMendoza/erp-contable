using System.Linq.Expressions;
using System.Reflection;
using ERP.BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Base para los DbContext de cada módulo:
/// <list type="bullet">
/// <item>filtro global por tenant en entidades <see cref="ITenantOwned"/>;</item>
/// <item>asignación/verificación del tenant al guardar;</item>
/// <item>transacción siempre explícita para que <see cref="TenantSessionInterceptor"/> fije el contexto RLS.</item>
/// </list>
/// Los filtros EF no sustituyen RLS ni la autorización de la API: son una capa adicional.
/// </summary>
public abstract class TenantAwareDbContext : DbContext
{
    private static readonly MethodInfo ApplyFilterMethod = typeof(TenantAwareDbContext)
        .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly ITenantContext _tenantContext;

    protected TenantAwareDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;

        // EF Core omite la transacción cuando SaveChanges emite una sola sentencia; sin transacción
        // no hay contexto RLS y la escritura sería rechazada. Se fuerza siempre.
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }

    /// <summary>Tenant actual usado por los filtros globales (EF lo parametriza en cada consulta).</summary>
    protected Guid? CurrentTenantId => _tenantContext.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType) && entityType.BaseType is null)
            {
                ApplyFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
    {
        Expression<Func<TEntity, bool>> filter = e => CurrentTenantId != null && e.TenantId == CurrentTenantId;
        modelBuilder.Entity<TEntity>().HasQueryFilter(filter);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOnWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantOnWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceTenantOnWrites()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            var tenantId = _tenantContext.TenantId ?? throw new TenantContextMissingException();
            var property = entry.Property(nameof(ITenantOwned.TenantId));

            if (entry.State == EntityState.Added && (Guid)property.CurrentValue! == Guid.Empty)
            {
                property.CurrentValue = tenantId;
            }

            if ((Guid)property.CurrentValue! != tenantId)
            {
                throw new InvalidOperationException(
                    $"Intento de escribir {entry.Metadata.ClrType.Name} de otro tenant. Operación bloqueada.");
            }
        }
    }
}
