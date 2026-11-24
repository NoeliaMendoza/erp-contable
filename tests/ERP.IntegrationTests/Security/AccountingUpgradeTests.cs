using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Tenancy;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Accounting.Data;
using ERP.Modules.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ERP.IntegrationTests.Security;

/// <summary>
/// Actualización del esquema contable con datos ya existentes (ACC-03). La migración
/// <c>AccountingPostingIntegrity</c> deduce <c>account_id</c> y <c>line_number</c> de las líneas
/// anteriores: debe conservar los datos, no inventar cuentas y dejar el aislamiento por empresa
/// tal como estaba. La prueba usa su propia base porque arranca desde la migración anterior.
/// </summary>
public sealed class AccountingUpgradeTests : IAsyncLifetime
{
    private const string PreviousMigration = "InitialAccountingPosting";
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly UpgradeDatabaseFixture _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(_db.StartAsync(Ct));

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private AccountingDbContext Accounting()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>();
        PersistenceRegistration.ConfigureNpgsql(options, _db.MigratorConnectionString, AccountingDbContext.Schema);
        var session = TenantSession.ForSystem();
        options.AddInterceptors(new TenantSessionInterceptor(session));
        return new AccountingDbContext(options.Options, session);
    }

    private PlatformDbContext Platform()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        PersistenceRegistration.ConfigureNpgsql(options, _db.MigratorConnectionString, PlatformDbContext.Schema);
        var session = TenantSession.ForSystem();
        options.AddInterceptors(new TenantSessionInterceptor(session));
        return new PlatformDbContext(options.Options, session);
    }

    private async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private static async Task SetTenantAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenant)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction);
        command.Parameters.AddWithValue("tenant", tenant.ToString());
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>
    /// Deja la base en el esquema anterior con dos empresas que repiten el mismo código de cuenta,
    /// para comprobar que el relleno enlaza cada línea con la cuenta de SU empresa.
    /// </summary>
    private async Task<(Guid EntryA, Guid EntryB, Guid CajaA, Guid VentasA, Guid CajaB)> SeedPreviousSchemaAsync()
    {
        await using (var platform = Platform())
        {
            await platform.Database.MigrateAsync(Ct);
        }

        await using (var accounting = Accounting())
        {
            await accounting.GetService<IMigrator>().MigrateAsync(PreviousMigration, Ct);
        }

        var cajaA = Guid.Parse("11111111-0000-0000-0000-00000000000a");
        var ventasA = Guid.Parse("22222222-0000-0000-0000-00000000000a");
        var cajaB = Guid.Parse("11111111-0000-0000-0000-00000000000b");
        var ventasB = Guid.Parse("22222222-0000-0000-0000-00000000000b");
        var entryA = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000a");
        var entryB = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000b");

        await using var connection = await OpenAsync(_db.MigratorConnectionString);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        const string Account = """
            INSERT INTO accounting.accounts
                (id, tenant_id, code, name, kind, normal_balance, parent_id, allows_posting, is_active, created_at_utc)
            VALUES (@id, @tenant, @code, @name, 'Asset', 'Debit', NULL, true, true, now())
            """;
        const string Entry = """
            INSERT INTO accounting.journal_entries
                (id, tenant_id, accounting_date, description, idempotency_key, source_module,
                 source_document_type, source_document_id, status, posted_at_utc)
            VALUES (@id, @tenant, '2026-03-15', @description, @key, 'sales', 'invoice', @document, 0, now())
            """;
        const string Line = """
            INSERT INTO accounting.journal_entry_lines
                (id, tenant_id, journal_entry_id, account_code, debit, credit, memo)
            VALUES (@id, @tenant, @entry, @code, @debit, @credit, NULLIF(@memo, ''))
            """;

        await SetTenantAsync(connection, transaction, TenantA);
        await ExecuteAsync(connection, transaction, Account, ("id", cajaA), ("tenant", TenantA), ("code", "1.1.01"), ("name", "Caja"));
        await ExecuteAsync(connection, transaction, Account, ("id", ventasA), ("tenant", TenantA), ("code", "4.1.01"), ("name", "Ventas"));
        await ExecuteAsync(connection, transaction, Entry, ("id", entryA), ("tenant", TenantA),
            ("description", "Venta anterior a la migracion"), ("key", "sales:invoice:anterior-a"), ("document", Guid.Parse("dddddddd-0000-0000-0000-00000000000a")));

        // Los ids crecen con la posición esperada: el relleno numera por (empresa, asiento) ordenando por id.
        await ExecuteAsync(connection, transaction, Line, ("id", Guid.Parse("0000000a-0000-0000-0000-000000000001")),
            ("tenant", TenantA), ("entry", entryA), ("code", "1.1.01"), ("debit", 20.00m), ("credit", 0.00m), ("memo", "Cobro"));
        await ExecuteAsync(connection, transaction, Line, ("id", Guid.Parse("0000000a-0000-0000-0000-000000000002")),
            ("tenant", TenantA), ("entry", entryA), ("code", "4.1.01"), ("debit", 0.00m), ("credit", 20.00m), ("memo", string.Empty));

        await SetTenantAsync(connection, transaction, TenantB);
        await ExecuteAsync(connection, transaction, Account, ("id", cajaB), ("tenant", TenantB), ("code", "1.1.01"), ("name", "Caja"));
        await ExecuteAsync(connection, transaction, Account, ("id", ventasB), ("tenant", TenantB), ("code", "4.1.01"), ("name", "Ventas"));
        await ExecuteAsync(connection, transaction, Entry, ("id", entryB), ("tenant", TenantB),
            ("description", "Venta anterior de otra empresa"), ("key", "sales:invoice:anterior-b"), ("document", Guid.Parse("dddddddd-0000-0000-0000-00000000000b")));
        await ExecuteAsync(connection, transaction, Line, ("id", Guid.Parse("0000000b-0000-0000-0000-000000000001")),
            ("tenant", TenantB), ("entry", entryB), ("code", "1.1.01"), ("debit", 5.50m), ("credit", 0.00m), ("memo", string.Empty));
        await ExecuteAsync(connection, transaction, Line, ("id", Guid.Parse("0000000b-0000-0000-0000-000000000002")),
            ("tenant", TenantB), ("entry", entryB), ("code", "4.1.01"), ("debit", 0.00m), ("credit", 5.50m), ("memo", string.Empty));
        await transaction.CommitAsync(Ct);

        return (entryA, entryB, cajaA, ventasA, cajaB);
    }

    [Fact]
    public async Task La_actualizacion_enlaza_las_lineas_existentes_conserva_los_datos_y_restablece_RLS()
    {
        var (entryA, entryB, cajaA, ventasA, cajaB) = await SeedPreviousSchemaAsync();

        await using (var accounting = Accounting())
        {
            await accounting.GetService<IMigrator>().MigrateAsync(cancellationToken: Ct);
        }

        await using var connection = await OpenAsync(_db.MigratorConnectionString);
        await using var transaction = await connection.BeginTransactionAsync(Ct);

        // Cada línea queda enlazada a la cuenta de su propia empresa, aunque el código se repita.
        await SetTenantAsync(connection, transaction, TenantA);
        await using (var lines = new NpgsqlCommand("""
            SELECT line_number, account_id, account_code, debit, credit, memo
              FROM accounting.journal_entry_lines WHERE journal_entry_id = @entry ORDER BY line_number
            """, connection, transaction))
        {
            lines.Parameters.AddWithValue("entry", entryA);
            await using var reader = await lines.ExecuteReaderAsync(Ct);
            Assert.True(await reader.ReadAsync(Ct));
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(cajaA, reader.GetGuid(1));
            Assert.Equal("1.1.01", reader.GetString(2));
            Assert.Equal(20.00m, reader.GetDecimal(3));
            Assert.Equal(0.00m, reader.GetDecimal(4));
            Assert.Equal("Cobro", reader.GetString(5));
            Assert.True(await reader.ReadAsync(Ct));
            Assert.Equal(2, reader.GetInt32(0));
            Assert.Equal(ventasA, reader.GetGuid(1));
            Assert.Equal(20.00m, reader.GetDecimal(4));
            Assert.True(await reader.IsDBNullAsync(5, Ct));
            Assert.False(await reader.ReadAsync(Ct));
        }

        await using (var description = new NpgsqlCommand(
            "SELECT description FROM accounting.journal_entries WHERE id = @id", connection, transaction))
        {
            description.Parameters.AddWithValue("id", entryA);
            Assert.Equal("Venta anterior a la migracion", await description.ExecuteScalarAsync(Ct));
        }

        await SetTenantAsync(connection, transaction, TenantB);
        await using (var foreign = new NpgsqlCommand("""
            SELECT account_id FROM accounting.journal_entry_lines
             WHERE journal_entry_id = @entry AND line_number = 1
            """, connection, transaction))
        {
            foreign.Parameters.AddWithValue("entry", entryB);
            Assert.Equal(cajaB, await foreign.ExecuteScalarAsync(Ct));
        }

        await transaction.RollbackAsync(Ct);

        foreach (var table in new[] { "accounting.accounts", "accounting.journal_entries", "accounting.journal_entry_lines" })
        {
            await using var security = new NpgsqlCommand($"""
                SELECT relrowsecurity, relforcerowsecurity,
                       (SELECT count(*) FROM pg_policies p
                         WHERE p.schemaname || '.' || p.tablename = '{table}' AND p.policyname = 'tenant_isolation')
                  FROM pg_class WHERE oid = '{table}'::regclass
                """, connection);
            await using var reader = await security.ExecuteReaderAsync(Ct);
            Assert.True(await reader.ReadAsync(Ct));
            Assert.True(reader.GetBoolean(0), $"{table} debe quedar con RLS habilitado tras migrar.");
            Assert.True(reader.GetBoolean(1), $"{table} debe quedar con RLS forzado tras migrar.");
            Assert.Equal(1L, reader.GetInt64(2));
        }

        await using var privileges = new NpgsqlCommand("""
            SELECT has_table_privilege('erp_runtime', 'accounting.journal_entries', 'UPDATE'),
                   has_table_privilege('erp_runtime', 'accounting.journal_entry_lines', 'DELETE'),
                   has_table_privilege('erp_runtime', 'accounting.journal_entries', 'SELECT'),
                   has_table_privilege('erp_runtime', 'accounting.accounts', 'UPDATE')
            """, connection);
        await using var grants = await privileges.ExecuteReaderAsync(Ct);
        Assert.True(await grants.ReadAsync(Ct));
        Assert.False(grants.GetBoolean(0), "El asiento no debe admitir UPDATE del rol runtime.");
        Assert.False(grants.GetBoolean(1), "La línea no debe admitir DELETE del rol runtime.");
        Assert.True(grants.GetBoolean(2));
        Assert.True(grants.GetBoolean(3), "El plan de cuentas conserva su CRUD.");
    }

    /// <summary>Una línea cuyo código no existe en el plan debe detener la migración, no quedar sin cuenta.</summary>
    [Fact]
    public async Task Una_linea_sin_cuenta_en_el_plan_detiene_la_actualizacion()
    {
        var (entryA, _, _, _, _) = await SeedPreviousSchemaAsync();
        await using (var connection = await OpenAsync(_db.MigratorConnectionString))
        {
            await using var transaction = await connection.BeginTransactionAsync(Ct);
            await SetTenantAsync(connection, transaction, TenantA);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO accounting.journal_entry_lines
                    (id, tenant_id, journal_entry_id, account_code, debit, credit, memo)
                VALUES (@id, @tenant, @entry, 'NO.EXISTE', 1.00, 0.00, NULL)
                """, ("id", Guid.Parse("0000000c-0000-0000-0000-000000000003")), ("tenant", TenantA), ("entry", entryA));
            await transaction.CommitAsync(Ct);
        }

        await using var accounting = Accounting();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            accounting.GetService<IMigrator>().MigrateAsync(cancellationToken: Ct));

        Assert.Contains("no se pueden enlazar al plan de cuentas", exception.MessageText, StringComparison.Ordinal);

        // La migración fallida no deja el esquema a medias: la columna sigue siendo opcional.
        await using var connection2 = await OpenAsync(_db.MigratorConnectionString);
        await using var column = new NpgsqlCommand("""
            SELECT count(*) FROM information_schema.columns
             WHERE table_schema = 'accounting' AND table_name = 'journal_entry_lines' AND column_name = 'account_id'
            """, connection2);
        Assert.Equal(0L, await column.ExecuteScalarAsync(Ct));
    }
}
