using Npgsql;
using Testcontainers.PostgreSql;

namespace ERP.IntegrationTests.Infrastructure;

/// <summary>
/// Base vacía con la topología de roles de producción, para probar una ACTUALIZACIÓN de esquema:
/// a diferencia de <see cref="PostgresFixture"/> no aplica migraciones ni semillas, de modo que la
/// prueba decida hasta qué migración llegar y qué datos existir antes de la siguiente.
/// </summary>
public sealed class UpgradeDatabaseFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _migratorPassword = Guid.NewGuid().ToString("N");
    private readonly string _appPassword = Guid.NewGuid().ToString("N");

    public string MigratorConnectionString { get; private set; } = string.Empty;

    public string AppConnectionString { get; private set; } = string.Empty;

    public async Task StartAsync(CancellationToken ct)
    {
        await _container.StartAsync(ct);
        var superuser = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());
        await using (var connection = new NpgsqlConnection(superuser.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await ExecuteAsync(connection, $"""
                CREATE ROLE erp_runtime NOLOGIN;
                CREATE ROLE erp_migrator LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB PASSWORD '{_migratorPassword}';
                CREATE ROLE erp_app LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB IN ROLE erp_runtime PASSWORD '{_appPassword}';
                """, ct);
            await ExecuteAsync(connection, "CREATE DATABASE erp OWNER erp_migrator;", ct);
            await ExecuteAsync(connection, "REVOKE ALL ON DATABASE erp FROM PUBLIC; GRANT CONNECT, TEMPORARY ON DATABASE erp TO erp_runtime;", ct);
        }

        MigratorConnectionString = Connection("erp_migrator", _migratorPassword, superuser.ConnectionString);
        AppConnectionString = Connection("erp_app", _appPassword, superuser.ConnectionString);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private static string Connection(string user, string password, string template) =>
        new NpgsqlConnectionStringBuilder(template) { Database = "erp", Username = user, Password = password }.ConnectionString;

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
