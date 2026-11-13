using ERP.BuildingBlocks.Security;
using ERP.Infrastructure.Tenancy;
using ERP.Modules.Accounting;
using ERP.Modules.Inventory;
using ERP.Modules.Platform;
using ERP.Modules.Platform.Models;
using ERP.Modules.Purchases;
using ERP.Modules.Reporting;
using ERP.Modules.Sales;
using Microsoft.EntityFrameworkCore;

namespace ERP.DbMigrator;

/// <summary>
/// Datos <b>ficticios</b> para desarrollo y pruebas: dos empresas aisladas y tres usuarios.
/// Los RUC son sintéticos y no corresponden intencionalmente a ningún contribuyente real.
/// Idempotente: si una empresa ya existe, no la vuelve a crear.
/// Como las tablas tienen FORCE ROW LEVEL SECURITY, cada empresa se siembra con su propio contexto de tenant.
/// </summary>
public sealed class DevelopmentSeeder(DatabaseMigrator migrator)
{
    public static readonly Guid TenantAndinaId = Guid.Parse("0195a000-0000-7000-8000-00000000a001");
    public static readonly Guid TenantPacificoId = Guid.Parse("0195a000-0000-7000-8000-00000000b001");

    public static readonly Guid AnaId = Guid.Parse("0195a000-0000-7000-8000-0000000000a1");
    public static readonly Guid BrunoId = Guid.Parse("0195a000-0000-7000-8000-0000000000b1");
    public static readonly Guid CarlaId = Guid.Parse("0195a000-0000-7000-8000-0000000000c1");

    public const string AnaEmail = "ana.admin@demo.local";
    public const string BrunoEmail = "bruno.admin@demo.local";
    public const string CarlaEmail = "carla.contadora@demo.local";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await SeedUsersAsync(now, cancellationToken);

        await SeedTenantAsync(new TenantSeed(
            TenantAndinaId,
            "Comercial Andina Demo S.A.",
            Features: ["platform", AccountingModule.Module, SalesModule.Module, InventoryModule.Module, ReportingModule.Module],
            Branches: [("MATRIZ", "Matriz Quito", "Quito"), ("NORTE", "Sucursal Norte", "Quito")],
            Ruc: "1799999990001",
            LegalName: "COMERCIAL ANDINA DEMO S.A.",
            KeepsAccounting: true,
            Admins: [AnaId, CarlaId],
            ReadOnly: []), now, cancellationToken);

        await SeedTenantAsync(new TenantSeed(
            TenantPacificoId,
            "Servicios Pacífico Demo",
            Features: ["platform", AccountingModule.Module, SalesModule.Module, PurchasesModule.Module, ReportingModule.Module],
            Branches: [("GYE", "Oficina Guayaquil", "Guayaquil")],
            Ruc: "0102030400001",
            LegalName: "PERSONA NATURAL DEMO PACIFICO",
            KeepsAccounting: false,
            Admins: [BrunoId],
            ReadOnly: [CarlaId]), now, cancellationToken);
    }

    private async Task SeedUsersAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var db = migrator.CreatePlatformContext(TenantSession.ForSystem());
        (Guid Id, string Email, string Name)[] users =
        [
            (AnaId, AnaEmail, "Ana Administradora (demo)"),
            (BrunoId, BrunoEmail, "Bruno Administrador (demo)"),
            (CarlaId, CarlaEmail, "Carla Contadora (demo)"),
        ];

        foreach (var (id, email, name) in users)
        {
            if (!await db.Users.AnyAsync(u => u.Id == id, ct))
            {
                db.Users.Add(AppUser.Create(id, email, name, now));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedTenantAsync(TenantSeed seed, DateTimeOffset now, CancellationToken ct)
    {
        var session = TenantSession.ForSystem(seed.TenantId);
        await using var db = migrator.CreatePlatformContext(session);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await db.Tenants.AnyAsync(t => t.Id == seed.TenantId, ct))
        {
            return;
        }

        db.Tenants.Add(Tenant.Create(seed.TenantId, seed.Name, now));
        foreach (var feature in seed.Features)
        {
            db.TenantFeatures.Add(TenantFeature.Create(seed.TenantId, feature, isEnabled: true));
        }

        var adminRole = Role.Create(Guid.CreateVersion7(), seed.TenantId, "Administrador", isSystem: true);
        var readRole = Role.Create(Guid.CreateVersion7(), seed.TenantId, "Consulta", isSystem: true);
        db.Roles.AddRange(adminRole, readRole);

        foreach (var code in AllPermissionCodes())
        {
            db.RolePermissions.Add(RolePermission.Create(seed.TenantId, adminRole.Id, code));
        }

        foreach (var code in PlatformPermissions.ReadOnly)
        {
            db.RolePermissions.Add(RolePermission.Create(seed.TenantId, readRole.Id, code));
        }

        Branch? first = null;
        foreach (var (code, name, city) in seed.Branches)
        {
            var branch = Branch.Create(Guid.CreateVersion7(), seed.TenantId, code, name, $"Dirección ficticia {code}", city, now, null);
            first ??= branch;
            db.Branches.Add(branch);
        }

        var taxpayer = Taxpayer.Create(
            Guid.CreateVersion7(), seed.TenantId, seed.Ruc, seed.LegalName, seed.Name, seed.KeepsAccounting,
            specialTaxpayerResolution: null, isWithholdingAgent: false, taxRegimeCode: null, validFrom: new DateOnly(2026, 1, 1));
        db.Taxpayers.Add(taxpayer);

        var establishment = Establishment.Create(Guid.CreateVersion7(), seed.TenantId, taxpayer.Id, first?.Id, "001", "Matriz", first?.Address);
        db.Establishments.Add(establishment);
        db.EmissionPoints.Add(EmissionPoint.Create(Guid.CreateVersion7(), seed.TenantId, establishment.Id, "001", "Caja principal"));

        AddMembers(db, seed.TenantId, seed.Admins, adminRole.Id, now);
        AddMembers(db, seed.TenantId, seed.ReadOnly, readRole.Id, now);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static void AddMembers(ERP.Modules.Platform.Data.PlatformDbContext db, Guid tenantId, IEnumerable<Guid> userIds, Guid roleId, DateTimeOffset now)
    {
        foreach (var userId in userIds)
        {
            var membership = TenantMembership.Create(Guid.CreateVersion7(), tenantId, userId, now);
            db.Memberships.Add(membership);
            db.MembershipRoles.Add(MembershipRole.Create(tenantId, membership.Id, roleId));
        }
    }

    internal static IEnumerable<string> AllPermissionCodes()
    {
        IPermissionProvider[] providers =
        [
            new PlatformPermissionProvider(),
            new AccountingPermissionProvider(),
            new SalesPermissionProvider(),
            new PurchasesPermissionProvider(),
            new InventoryPermissionProvider(),
            new ReportingPermissionProvider(),
        ];
        return providers.SelectMany(p => p.GetPermissions()).Select(p => p.Code).Distinct();
    }

    private sealed record TenantSeed(
        Guid TenantId,
        string Name,
        string[] Features,
        (string Code, string Name, string City)[] Branches,
        string Ruc,
        string LegalName,
        bool KeepsAccounting,
        Guid[] Admins,
        Guid[] ReadOnly);
}
