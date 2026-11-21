using Npgsql;

namespace ERP.IntegrationTests.Infrastructure;

/// <summary>La migración contable es obligatoria: su ausencia debe fallar, nunca omitir las pruebas.</summary>
internal static class AccountingSchemaPrerequisite
{
    public static async Task RequireAsync(PostgresFixture db, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(db.MigratorConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT to_regclass('accounting.accounts')::text", connection);
        var table = await command.ExecuteScalarAsync(ct);
        Assert.True(table is string, "Falta accounting.accounts: revise InitialAccounting y su registro en DatabaseMigrator. Las pruebas no crean tablas por fuera de las migraciones.");
    }
}
