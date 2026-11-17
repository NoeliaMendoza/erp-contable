using ERP.Infrastructure.Persistence;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Inventory.Services;

/// <summary>
/// Implementación del contrato <see cref="IProductCatalog"/> para otros módulos (p. ej. Ventas de Casas):
/// devuelve solo artículos de la empresa activa. IDs ajenos o inexistentes simplemente no aparecen.
/// </summary>
internal sealed class ProductCatalog(InventoryDbContext db) : IProductCatalog
{
    public Task<IReadOnlyList<ProductSummary>> GetByIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync<IReadOnlyList<ProductSummary>>(async ct =>
            await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Join(db.Units, p => p.UnitId, u => u.Id, (p, u) =>
                    new ProductSummary(p.Id, p.Sku, p.Name, p.Kind == ProductKind.Service, u.Code, p.IsActive))
                .ToListAsync(ct),
            cancellationToken);
}
