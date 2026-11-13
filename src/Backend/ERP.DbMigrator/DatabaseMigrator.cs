using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Tenancy;
using ERP.Modules.Platform.Data;
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
        await using var platform = CreatePlatformContext(TenantSession.ForSystem());
        await platform.Database.MigrateAsync(cancellationToken);
    }

    internal PlatformDbContext CreatePlatformContext(TenantSession session)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        PersistenceRegistration.ConfigureNpgsql(options, migratorConnectionString, PlatformDbContext.Schema);
        options.AddInterceptors(new TenantSessionInterceptor(session));
        return new PlatformDbContext(options.Options, session);
    }
}
