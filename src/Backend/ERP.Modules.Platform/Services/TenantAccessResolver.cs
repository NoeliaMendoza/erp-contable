using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Platform.Data;
using ERP.Modules.Platform.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Modules.Platform.Services;

/// <summary>
/// Valida membresía activa + tenant activo y calcula permisos efectivos.
/// Precondición: la sesión ya contiene el usuario y el tenant <b>candidato</b> (lo fija el middleware),
/// de modo que RLS solo deja ver filas de ese tenant o del propio usuario.
/// </summary>
internal sealed class TenantAccessResolver(PlatformDbContext db) : ITenantAccessResolver
{
    public Task<TenantAccessResult> ResolveAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var membershipId = await db.Memberships.AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.UserId == userId && m.Status == MembershipStatus.Active)
                .Join(
                    db.Tenants.Where(t => t.Status == TenantStatus.Active),
                    m => m.TenantId,
                    t => t.Id,
                    (m, _) => (Guid?)m.Id)
                .SingleOrDefaultAsync(ct);

            if (membershipId is null)
            {
                return TenantAccessResult.Denied;
            }

            var permissions = await (
                    from mr in db.MembershipRoles
                    where mr.MembershipId == membershipId
                    join rp in db.RolePermissions on mr.RoleId equals rp.RoleId
                    select rp.PermissionCode)
                .Distinct()
                .ToListAsync(ct);

            return new TenantAccessResult(true, permissions);
        }, cancellationToken);
}

internal sealed class UserDirectory(PlatformDbContext db) : IUserDirectory
{
    public async Task<Guid?> FindActiveUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = AppUser.NormalizeEmail(email);
        return await db.Users.AsNoTracking()
            .Where(u => u.Email == normalized && u.IsActive)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
