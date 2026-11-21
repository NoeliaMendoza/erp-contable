using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Tenancy;
using ERP.Modules.Accounting.Data;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Platform.Data;
using ERP.Modules.Purchases.Data;
using ERP.Modules.Sales.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.DbMigrator;

/// <summary>
/// Aplica las migraciones de todos los DbContext de módulos, en orden de dependencia.
/// Cada módulo nuevo con persistencia se agrega aquí (Platform primero: crea app_security).
/// </summary>
public sealed class DatabaseMigrator(string migratorConnectionString)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using (var platform = CreatePlatformContext(TenantSession.ForSystem()))
        {
            await platform.Database.MigrateAsync(cancellationToken);
        }

        // Credenciales (Identity) y almacén OpenIddict: después de Platform porque referencia platform.users.
        await using (var auth = CreateAuthContext())
        {
            await auth.Database.MigrateAsync(cancellationToken);
        }

        await using var inventory = CreateInventoryContext(TenantSession.ForSystem());
        await inventory.Database.MigrateAsync(cancellationToken);

        await using var sales = CreateSalesContext(TenantSession.ForSystem());
        await sales.Database.MigrateAsync(cancellationToken);
        await using var accounting = CreateAccountingContext(TenantSession.ForSystem());
        await accounting.Database.MigrateAsync(cancellationToken);

        await using var purchases = CreatePurchasesContext(TenantSession.ForSystem());
        await purchases.Database.MigrateAsync(cancellationToken);
    }

    internal PlatformDbContext CreatePlatformContext(TenantSession session)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        PersistenceRegistration.ConfigureNpgsql(options, migratorConnectionString, PlatformDbContext.Schema);
        options.AddInterceptors(new TenantSessionInterceptor(session));
        return new PlatformDbContext(options.Options, session);
    }

    internal AuthDbContext CreateAuthContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>();
        AuthDbContext.Configure(options, migratorConnectionString);
        return new AuthDbContext(options.Options);
    }

    internal InventoryDbContext CreateInventoryContext(TenantSession session)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>();
        PersistenceRegistration.ConfigureNpgsql(options, migratorConnectionString, InventoryDbContext.Schema);
        options.AddInterceptors(new TenantSessionInterceptor(session));
        return new InventoryDbContext(options.Options, session);
    }

    internal SalesDbContext CreateSalesContext(TenantSession session)
    {
        var options = new DbContextOptionsBuilder<SalesDbContext>();
        PersistenceRegistration.ConfigureNpgsql(options, migratorConnectionString, SalesDbContext.Schema);
        options.AddInterceptors(new TenantSessionInterceptor(session));
        return new SalesDbContext(options.Options, session);
    }

    internal AccountingDbContext CreateAccountingContext(TenantSession session)
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>();
        PersistenceRegistration.ConfigureNpgsql(options, migratorConnectionString, AccountingDbContext.Schema);
        options.AddInterceptors(new TenantSessionInterceptor(session));
        return new AccountingDbContext(options.Options, session);
    }

    internal PurchasesDbContext CreatePurchasesContext(TenantSession session)
    {
        var options = new DbContextOptionsBuilder<PurchasesDbContext>();
        PersistenceRegistration.ConfigureNpgsql(options, migratorConnectionString, PurchasesDbContext.Schema);
        options.AddInterceptors(new TenantSessionInterceptor(session));
        return new PurchasesDbContext(options.Options, session);
    }
}
