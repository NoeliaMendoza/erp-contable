using ERP.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace ERP.DbMigrator;

/// <summary>
/// Uso:
/// <code>
///   dotnet run --project src/Backend/ERP.DbMigrator -- migrate            (aplica migraciones)
///   dotnet run --project src/Backend/ERP.DbMigrator -- migrate seed-dev   (migra y carga datos ficticios)
/// </code>
/// Conexión: ConnectionStrings:ErpMigrator (User Secrets o variable ConnectionStrings__ErpMigrator).
/// </summary>
internal static class MigratorProgram
{
    public static async Task<int> Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<DatabaseMigrator>(optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToArray())
            .Build();

        var connectionString = configuration.GetConnectionString(PersistenceRegistration.MigratorConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Falta ConnectionStrings:{PersistenceRegistration.MigratorConnectionName}. Ver README > Base de datos local.");
            return 1;
        }

        var commands = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (commands.Count == 0)
        {
            commands.Add("migrate");
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var migrator = new DatabaseMigrator(connectionString);

        if (commands.Contains("migrate"))
        {
            await migrator.MigrateAsync(cts.Token);
            Console.WriteLine("Migraciones aplicadas.");
        }

        if (commands.Contains("seed-dev"))
        {
            await new DevelopmentSeeder(migrator).SeedAsync(cts.Token);
            Console.WriteLine("Semillas de desarrollo cargadas (datos ficticios).");
        }

        return 0;
    }
}
