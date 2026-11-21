using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Purchases.Data;
using ERP.Modules.Purchases.Dtos;
using ERP.Modules.Purchases.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Modules.Purchases.Services;

/// <summary>Casos de uso del catálogo de proveedores dentro del tenant activo.</summary>
public sealed class SupplierService(PurchasesDbContext db, ITenantContext tenant, TimeProvider clock)
{
    private const string DuplicateConstraint = "ux_suppliers_tenant_identification";

    public Task<PagedResult<SupplierResponse>> ListAsync(PageRequest page, bool? isActive, CancellationToken ct) =>
        db.InTenantTransactionAsync(async token =>
        {
            var query = db.Suppliers.AsNoTracking();
            if (isActive is not null)
            {
                query = query.Where(s => s.IsActive == isActive.Value);
            }

            if (page.Term is { } term)
            {
                var pattern = Contains(term);
                query = query.Where(s => EF.Functions.ILike(s.Name, pattern, "\\")
                    || EF.Functions.ILike(s.IdentificationNumber, pattern, "\\"));
            }

            var total = await query.CountAsync(token);
            var items = await query.OrderBy(s => s.Name).ThenBy(s => s.Id)
                .Skip(page.Skip).Take(page.SafePageSize)
                .Select(s => new SupplierResponse(s.Id, s.IdentificationType, s.IdentificationNumber,
                    s.Name, s.Email, s.Phone, s.Address, s.IsActive))
                .ToListAsync(token);
            return new PagedResult<SupplierResponse>(items, total, page.SafePage, page.SafePageSize);
        }, ct);

    public Task<SupplierResponse> GetAsync(Guid id, CancellationToken ct) =>
        db.InTenantTransactionAsync(async token => ToResponse(await FindAsync(id, token)), ct);

    public Task<SupplierResponse> CreateAsync(SaveSupplierRequest request, CancellationToken ct) =>
        db.InTenantTransactionAsync(async token =>
        {
            var supplier = Supplier.Create(Guid.CreateVersion7(), tenant.RequireTenantId(), ToData(request), clock.GetUtcNow(), tenant.UserId);
            await EnsureAvailableAsync(supplier, null, token);
            db.Suppliers.Add(supplier);
            await SaveAsync(supplier, token);
            return ToResponse(supplier);
        }, ct);

    public Task<SupplierResponse> UpdateAsync(Guid id, SaveSupplierRequest request, CancellationToken ct) =>
        db.InTenantTransactionAsync(async token =>
        {
            var supplier = await FindAsync(id, token);
            var data = ToData(request);
            var candidate = Supplier.Create(Guid.CreateVersion7(), supplier.TenantId, data, clock.GetUtcNow(), tenant.UserId);
            await EnsureAvailableAsync(candidate, id, token);
            supplier.Update(data, clock.GetUtcNow(), tenant.UserId);
            await SaveAsync(supplier, token);
            return ToResponse(supplier);
        }, ct);

    public Task<SupplierResponse> SetStatusAsync(Guid id, bool isActive, CancellationToken ct) =>
        db.InTenantTransactionAsync(async token =>
        {
            var supplier = await FindAsync(id, token);
            supplier.SetActive(isActive, clock.GetUtcNow(), tenant.UserId);
            await db.SaveChangesAsync(token);
            return ToResponse(supplier);
        }, ct);

    private async Task<Supplier> FindAsync(Guid id, CancellationToken ct) =>
        await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, ct)
        ?? throw new NotFoundException("Proveedor", id);

    private async Task EnsureAvailableAsync(Supplier supplier, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Suppliers.AnyAsync(s => s.IdentificationType == supplier.IdentificationType
            && s.IdentificationNumber == supplier.IdentificationNumber && s.Id != exceptId, ct))
        {
            throw Duplicate(supplier);
        }
    }

    private async Task SaveAsync(Supplier supplier, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: DuplicateConstraint })
        {
            throw Duplicate(supplier);
        }
    }

    private static ConflictException Duplicate(Supplier supplier) =>
        new("supplier_identification_duplicated", "Ya existe un proveedor con esa identificación en esta empresa.");

    private static SupplierData ToData(SaveSupplierRequest request) => new(
        request.IdentificationType,
        request.IdentificationNumber,
        request.Name,
        request.Email,
        request.Phone,
        request.Address);

    private static SupplierResponse ToResponse(Supplier supplier) => new(
        supplier.Id,
        supplier.IdentificationType,
        supplier.IdentificationNumber,
        supplier.Name,
        supplier.Email,
        supplier.Phone,
        supplier.Address,
        supplier.IsActive);

    private static string Contains(string term) =>
        "%" + term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
