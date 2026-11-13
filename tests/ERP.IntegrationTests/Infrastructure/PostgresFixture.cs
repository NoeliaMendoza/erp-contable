using ERP.DbMigrator;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ERP.IntegrationTests.Infrastructure;

/// <summary>
/// PostgreSQL efímero con la misma topología de roles que producción:
/// superusuario solo para preparar; migraciones con erp_migrator; la API con erp_app (sin BYPASSRLS).
/// Las contraseñas son aleatorias y solo viven mientras dura el contenedor.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly string _migratorPassword = Guid.NewGuid().ToString("N");
    private readonly string _appPassword = Guid.NewGuid().ToString("N");

    public string MigratorConnectionString { get; private set; } = string.Empty;

    public string AppConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var superuser = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());
        await using (var connection = new NpgsqlConnection(superuser.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, $"""
                CREATE ROLE erp_runtime NOLOGIN;
                CREATE ROLE erp_migrator LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB PASSWORD '{_migratorPassword}';
                CREATE ROLE erp_app LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB IN ROLE erp_runtime PASSWORD '{_appPassword}';
                """);
            await ExecuteAsync(connection, "CREATE DATABASE erp OWNER erp_migrator;");
            await ExecuteAsync(connection, "REVOKE ALL ON DATABASE erp FROM PUBLIC; GRANT CONNECT, TEMPORARY ON DATABASE erp TO erp_runtime;");
        }

        MigratorConnectionString = new NpgsqlConnectionStringBuilder(superuser.ConnectionString)
        {
            Database = "erp",
            Username = "erp_migrator",
            Password = _migratorPassword,
        }.ConnectionString;

        AppConnectionString = new NpgsqlConnectionStringBuilder(superuser.ConnectionString)
        {
            Database = "erp",
            Username = "erp_app",
            Password = _appPassword,
        }.ConnectionString;

        var migrator = new DatabaseMigrator(MigratorConnectionString);
        await migrator.MigrateAsync(CancellationToken.None);
        await new DevelopmentSeeder(migrator).SeedAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
