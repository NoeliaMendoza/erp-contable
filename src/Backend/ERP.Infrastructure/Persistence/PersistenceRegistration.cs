using ERP.BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace ERP.Infrastructure.Persistence;

public static class PersistenceRegistration
{
    /// <summary>Cadena de conexión del rol runtime (sin privilegios DDL ni BYPASSRLS).</summary>
    public const string RuntimeConnectionName = "ErpRuntime";

    /// <summary>Cadena de conexión del rol de migraciones (solo DbMigrator / CI; nunca la API).</summary>
    public const string MigratorConnectionName = "ErpMigrator";

    /// <summary>
    /// Registra el DbContext de un módulo con la configuración común: Npgsql, snake_case,
    /// historial de migraciones en el esquema del módulo e interceptor de contexto RLS.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : DbContext
    {
        var connectionString = configuration.GetConnectionString(RuntimeConnectionName)
            ?? throw new InvalidOperationException(
                $"Falta ConnectionStrings:{RuntimeConnectionName}. Configúrela con User Secrets o variables de entorno (ver README).");

        services.AddDbContext<TContext>((sp, options) =>
        {
            ConfigureNpgsql(options, connectionString, schema);
            options.AddInterceptors(new TenantSessionInterceptor(sp.GetRequiredService<ITenantContext>()));
        });

        return services;
    }

    public static void ConfigureNpgsql(DbContextOptionsBuilder options, string connectionString, string schema, Action<NpgsqlDbContextOptionsBuilder>? configure = null)
    {
        options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", schema);
                configure?.Invoke(npgsql);
            })
            .UseSnakeCaseNamingConvention();
    }
}
