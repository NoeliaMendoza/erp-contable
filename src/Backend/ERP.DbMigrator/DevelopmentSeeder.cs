using ERP.BuildingBlocks.Security;
using ERP.Infrastructure.Tenancy;
using ERP.Modules.Accounting;
using ERP.Modules.Inventory;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Inventory.Models;
using ERP.Modules.Inventory.Services;
using ERP.Modules.Platform;
using ERP.Modules.Platform.Models;
using ERP.Modules.Purchases;
using ERP.Modules.Reporting;
using ERP.Modules.Sales;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ERP.DbMigrator;

/// <summary>
/// Datos <b>ficticios</b> para desarrollo y pruebas: dos empresas aisladas y tres usuarios.
/// Los RUC son sintéticos y no corresponden intencionalmente a ningún contribuyente real.
/// Idempotente: si una empresa ya existe, no la vuelve a crear.
/// Como las tablas tienen FORCE ROW LEVEL SECURITY, cada empresa se siembra con su propio contexto de tenant.
/// </summary>
/// <param name="migrator">Crea los DbContext con el rol de migraciones.</param>
/// <param name="demoPassword">
/// Contraseña de los usuarios demo para el inicio de sesión real (OIDC). Se toma de <see cref="DemoPasswordKey"/>
/// (User Secrets o variable de entorno) y nunca se versiona. Si es nula no se crean credenciales.
/// </param>
public sealed class DevelopmentSeeder(DatabaseMigrator migrator, string? demoPassword = null)
{
    /// <summary>Clave de configuración de la contraseña de los usuarios demo (<c>Seed__DemoPassword</c> como variable).</summary>
    public const string DemoPasswordKey = "Seed:DemoPassword";

    public static readonly Guid TenantAndinaId = Guid.Parse("0195a000-0000-7000-8000-00000000a001");
    public static readonly Guid TenantPacificoId = Guid.Parse("0195a000-0000-7000-8000-00000000b001");

    public static readonly Guid AnaId = Guid.Parse("0195a000-0000-7000-8000-0000000000a1");
    public static readonly Guid BrunoId = Guid.Parse("0195a000-0000-7000-8000-0000000000b1");
    public static readonly Guid CarlaId = Guid.Parse("0195a000-0000-7000-8000-0000000000c1");

    public const string AnaEmail = "ana.admin@demo.local";
    public const string BrunoEmail = "bruno.admin@demo.local";
    public const string CarlaEmail = "carla.contadora@demo.local";
    public static readonly Guid MainWarehouseId = Guid.Parse("0195a000-0000-7000-8000-00000000f001");

    /// <summary>Documento ficticio del saldo inicial de demostración (no existe en Compras).</summary>
    public static readonly Guid OpeningReceiptId = Guid.Parse("0195a000-0000-7000-8000-00000000f101");

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await SeedUsersAsync(now, cancellationToken);
        await SeedCredentialsAsync(cancellationToken);

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

        await SeedInventoryAsync(TenantAndinaId, now, cancellationToken);
    }

    private static async Task SyncSystemRolePermissionsAsync(ERP.Modules.Platform.Data.PlatformDbContext db, Guid tenantId, CancellationToken ct)
    {
        var roles = await db.Roles.Where(r => r.IsSystem).ToListAsync(ct);
        foreach (var role in roles)
        {
            IEnumerable<string> expected = role.Name switch
            {
                "Administrador" => AllPermissionCodes(),
                "Consulta" => ReadOnlyPermissionCodes,
                _ => [],
            };

            var existing = await db.RolePermissions.Where(rp => rp.RoleId == role.Id).Select(rp => rp.PermissionCode).ToListAsync(ct);
            foreach (var code in expected.Except(existing, StringComparer.Ordinal))
            {
                db.RolePermissions.Add(RolePermission.Create(tenantId, role.Id, code));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Permisos del rol "Consulta": solo lectura.</summary>
    internal static readonly string[] ReadOnlyPermissionCodes =
    [
        .. PlatformPermissions.ReadOnly,
        AccountingPermissions.AccountsRead,
        InventoryPermissions.UnitsRead,
        InventoryPermissions.ProductsRead,
        InventoryPermissions.CategoriesRead,
        InventoryPermissions.KardexRead,
        SalesPermissions.CustomersRead,
        PurchasesPermissions.SuppliersRead,
    ];

    /// <summary>Catálogo ficticio mínimo (ejemplo del plan: cuadernos a USD 5) para la empresa que vende productos.</summary>
    private async Task SeedInventoryAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var session = TenantSession.ForSystem(tenantId);
        await using var db = migrator.CreateInventoryContext(session);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!await db.Units.AnyAsync(ct))
        {
            var und = Unit.Create(Guid.CreateVersion7(), tenantId, "UND", "Unidad", now, null);
            var hora = Unit.Create(Guid.CreateVersion7(), tenantId, "HORA", "Hora", now, null);
            db.Units.AddRange(und, hora);
            var cuaderno = Product.Create(Guid.CreateVersion7(), tenantId,
                    new ProductData("CUAD-001", "Cuaderno universitario 100 hojas", ProductKind.Product, und.Id, 5.00m,
                        VatTreatment.ZeroRated, Barcode: "7860000000017", Cost: 3.00m, MinStock: 10m), now, null);
            var servicio = Product.Create(Guid.CreateVersion7(), tenantId,
                    new ProductData("SRV-INST", "Servicio de instalación", ProductKind.Service, hora.Id, 25.00m, VatTreatment.Standard),
                    now, null);
            db.Products.AddRange(cuaderno, servicio);
            await db.SaveChangesAsync(ct);

            db.Warehouses.Add(Warehouse.Create(MainWarehouseId, tenantId, Warehouse.DefaultCode, Warehouse.DefaultName, isDefault: true, now, null));
            await db.SaveChangesAsync(ct);

            // El stock nunca se digita: el saldo inicial entra como una recepción de compra ficticia (10 a USD 3).
            await new InventoryStockService(db, session, TimeProvider.System).RegisterAsync(new InventoryMovementRequest(
                $"purchases:receipt:{OpeningReceiptId:D}", "purchases", "receipt", OpeningReceiptId, MainWarehouseId,
                InventoryMovementKind.Entrada, DateOnly.FromDateTime(now.UtcDateTime),
                [new InventoryMovementLine(cuaderno.Id, 10m, 3.00m)]), ct);
        }

        // Categorías: aparte, para que también se agreguen en bases sembradas antes de que existieran (ramas de Neon).
        if (!await db.Categories.AnyAsync(ct))
        {
            var papeleria = Category.Create(Guid.CreateVersion7(), tenantId, "Papelería", now, null);
            var servicios = Category.Create(Guid.CreateVersion7(), tenantId, "Servicios técnicos", now, null);
            db.Categories.AddRange(papeleria, servicios);
            await db.SaveChangesAsync(ct);

            await AssignCategoryAsync(db, "CUAD-001", papeleria.Id, now, ct);
            await AssignCategoryAsync(db, "SRV-INST", servicios.Id, now, ct);
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static async Task AssignCategoryAsync(InventoryDbContext db, string sku, Guid categoryId, DateTimeOffset now, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Sku == sku && p.CategoryId == null, ct);
        product?.Update(
            new ProductData(product.Sku, product.Name, product.Kind, product.UnitId, product.Price, product.VatTreatment,
                categoryId, product.Barcode, product.Description, product.Cost, product.TracksInventory, product.MinStock),
            now, null);
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

    /// <summary>
    /// Credenciales de Identity para los usuarios demo (mismo Id que <c>platform.users</c>). Si cambia la contraseña
    /// configurada, se actualiza el hash. Nunca se escribe la contraseña en consola ni en registros.
    /// </summary>
    private async Task SeedCredentialsAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(demoPassword))
        {
            return;
        }

        if (demoPassword.Length < 10)
        {
            throw new InvalidOperationException($"{DemoPasswordKey} debe tener al menos 10 caracteres.");
        }

        var hasher = new PasswordHasher<ErpIdentityUser>();
        await using var db = migrator.CreateAuthContext();

        foreach (var (id, email) in new[] { (AnaId, AnaEmail), (BrunoId, BrunoEmail), (CarlaId, CarlaEmail) })
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is null)
            {
                user = new ErpIdentityUser(id, email);
                user.NormalizedEmail = user.Email!.ToUpperInvariant();
                user.NormalizedUserName = user.UserName!.ToUpperInvariant();
                user.PasswordHash = hasher.HashPassword(user, demoPassword);
                db.Users.Add(user);
            }
            else if (user.PasswordHash is null
                || hasher.VerifyHashedPassword(user, user.PasswordHash, demoPassword) == PasswordVerificationResult.Failed)
            {
                user.PasswordHash = hasher.HashPassword(user, demoPassword);
                user.SecurityStamp = Guid.NewGuid().ToString("N");
                user.LockoutEnd = null;
                user.AccessFailedCount = 0;
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
            // Empresa ya sembrada: solo agrega a los roles del sistema los permisos nuevos que publiquen los módulos.
            await SyncSystemRolePermissionsAsync(db, seed.TenantId, ct);
            await tx.CommitAsync(ct);
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

        foreach (var code in ReadOnlyPermissionCodes)
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
