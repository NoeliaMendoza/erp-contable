using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Platform.Data;
using ERP.Modules.Platform.Dtos;
using ERP.Modules.Platform.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Modules.Platform.Services;

/// <summary>Casos de uso del módulo Platform. Toda lectura tenant-scoped ocurre dentro de una transacción (contexto RLS).</summary>
public sealed class PlatformService(PlatformDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public Task<MyProfileResponse> GetMyProfileAsync(CancellationToken cancellationToken)
    {
        var userId = tenant.UserId ?? throw new UnauthorizedAccessException();

        return db.InTenantTransactionAsync(async ct =>
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);

            // Sin tenant activo: RLS solo muestra membresías del propio usuario y sus tenants.
            var memberships = await db.Memberships.AsNoTracking()
                .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
                .Join(db.Tenants.Where(t => t.Status == TenantStatus.Active), m => m.TenantId, t => t.Id, (m, t) => t)
                .OrderBy(t => t.Name)
                .Select(t => new MembershipResponse(t.Id, t.Name))
                .ToListAsync(ct);

            return new MyProfileResponse(user.Id, user.Email, user.DisplayName, memberships);
        }, cancellationToken);
    }

    public Task<CurrentTenantResponse> GetCurrentTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenantId();

        return db.InTenantTransactionAsync(async ct =>
        {
            var entity = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, ct)
                ?? throw new NotFoundException("Empresa", tenantId);

            var features = await db.TenantFeatures.AsNoTracking()
                .Where(f => f.IsEnabled)
                .Select(f => f.FeatureCode)
                .OrderBy(f => f)
                .ToListAsync(ct);

            return new CurrentTenantResponse(
                entity.Id,
                entity.Name,
                entity.Status.ToString(),
                features,
                [.. tenant.Permissions.Order(StringComparer.Ordinal)]);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<BranchResponse>> ListBranchesAsync(CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync<IReadOnlyList<BranchResponse>>(async ct =>
            await db.Branches.AsNoTracking()
                .OrderBy(b => b.Code)
                .Select(b => new BranchResponse(b.Id, b.Code, b.Name, b.Address, b.City, b.IsActive))
                .ToListAsync(ct),
            cancellationToken);

    public Task<BranchResponse> GetBranchAsync(Guid id, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
            await db.Branches.AsNoTracking()
                .Where(b => b.Id == id)
                .Select(b => new BranchResponse(b.Id, b.Code, b.Name, b.Address, b.City, b.IsActive))
                .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Sucursal", id),
            cancellationToken);

    public async Task<BranchResponse> CreateBranchAsync(CreateBranchRequest request, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenantId();
        var branch = Branch.Create(
            Guid.CreateVersion7(), tenantId, request.Code, request.Name, request.Address, request.City,
            clock.GetUtcNow(), tenant.UserId);

        db.Branches.Add(branch);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DomainException("branch_code_duplicated", $"Ya existe una sucursal con el código '{branch.Code}'.");
        }

        return new BranchResponse(branch.Id, branch.Code, branch.Name, branch.Address, branch.City, branch.IsActive);
    }

    public Task<IReadOnlyList<TaxpayerResponse>> ListTaxpayersAsync(CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync<IReadOnlyList<TaxpayerResponse>>(async ct =>
        {
            var taxpayers = await db.Taxpayers.AsNoTracking().OrderBy(t => t.Ruc).ToListAsync(ct);
            var establishments = await db.Establishments.AsNoTracking().OrderBy(e => e.Code).ToListAsync(ct);
            var points = await db.EmissionPoints.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct);

            return [.. taxpayers.Select(t => new TaxpayerResponse(
                t.Id, t.Ruc, t.LegalName, t.TradeName, t.Kind.ToString(), t.KeepsAccounting, t.IsWithholdingAgent,
                t.SpecialTaxpayerResolution, t.ValidFrom,
                [.. establishments.Where(e => e.TaxpayerId == t.Id).Select(e => new EstablishmentResponse(
                    e.Id, e.Code, e.Name, e.Address, e.BranchId,
                    [.. points.Where(p => p.EstablishmentId == e.Id).Select(p => new EmissionPointResponse(p.Id, p.Code, p.Description, p.IsActive))]))]))];
        }, cancellationToken);
}
