using System.Net;
using System.Net.Http.Json;
using ERP.DbMigrator;
using ERP.Infrastructure.Tenancy;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Accounting.Contracts;
using ERP.Modules.Accounting.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ERP.IntegrationTests.Security;

/// <summary>
/// ACC-03: un asiento contabilizado no se modifica ni se borra. La garantía es del motor:
/// el rol runtime solo tiene SELECT e INSERT sobre las tablas del diario.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class JournalImmutabilityTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly Guid A = DevelopmentSeeder.TenantAndinaId;
    private static readonly Guid B = DevelopmentSeeder.TenantPacificoId;

    private ErpApiFactory _api = null!;
    private HttpClient _ana = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        _api = new ErpApiFactory(db);
        _ana = _api.ClientFor(DevelopmentSeeder.AnaEmail, A);
    }

    public async ValueTask DisposeAsync()
    {
        _ana.Dispose();
        await _api.DisposeAsync();
    }

    private async Task<string> AccountAsync()
    {
        var code = "I" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        using var response = await _ana.PostAsJsonAsync("/api/v1/accounts",
            new SaveAccountRequest(code, "Cuenta inmutable", "Asset", "Debit", null, true), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(Ct))!.Code;
    }

    private async Task<Guid> PostAsync()
    {
        var debit = await AccountAsync();
        var credit = await AccountAsync();
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantSession>().ConfirmTenant(A, []);
        var result = await scope.ServiceProvider.GetRequiredService<IAccountingPostingService>()
            .PostAsync(new PostingRequest("inmutable:" + Guid.NewGuid().ToString("N"), "sales", "invoice",
                Guid.CreateVersion7(), new DateOnly(2026, 9, 9), "Asiento inmutable",
                [new(debit, 15m, 0m, null), new(credit, 0m, 15m, null)]), Ct);
        return result.JournalEntryId;
    }

    private async Task<NpgsqlConnection> OpenAppAsync()
    {
        var connection = new NpgsqlConnection(db.AppConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private static async Task SetTenantAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenant)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction);
        command.Parameters.AddWithValue("tenant", tenant.ToString());
        await command.ExecuteNonQueryAsync(Ct);
    }

    [Theory]
    [InlineData("UPDATE accounting.journal_entries SET description = 'Alterado' WHERE id = @id")]
    [InlineData("DELETE FROM accounting.journal_entries WHERE id = @id")]
    [InlineData("UPDATE accounting.journal_entry_lines SET debit = 999 WHERE journal_entry_id = @id")]
    [InlineData("DELETE FROM accounting.journal_entry_lines WHERE journal_entry_id = @id")]
    public async Task El_rol_runtime_no_puede_modificar_ni_borrar_un_asiento(string sql)
    {
        var id = await PostAsync();
        await using var connection = await OpenAppAsync();
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await SetTenantAsync(connection, transaction, A);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        await transaction.RollbackAsync(Ct);
        var unchanged = await _ana.GetFromJsonAsync<JournalEntryResponse>($"/api/v1/journal-entries/{id}", Ct);
        Assert.Equal("Asiento inmutable", unchanged!.Description);
        Assert.Equal(15m, unchanged.TotalDebit);
    }

    [Fact]
    public async Task Las_tablas_del_diario_tienen_RLS_forzado_y_solo_select_insert_para_el_runtime()
    {
        await using var connection = await OpenAppAsync();
        foreach (var table in new[] { "accounting.journal_entries", "accounting.journal_entry_lines" })
        {
            await using var security = new NpgsqlCommand(
                $"SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = '{table}'::regclass", connection);
            await using (var reader = await security.ExecuteReaderAsync(Ct))
            {
                Assert.True(await reader.ReadAsync(Ct));
                Assert.True(reader.GetBoolean(0), $"{table} debe tener RLS habilitado.");
                Assert.True(reader.GetBoolean(1), $"{table} debe tener RLS forzado.");
            }

            await using var grants = new NpgsqlCommand($"""
                SELECT has_table_privilege('erp_runtime', '{table}', 'SELECT'),
                       has_table_privilege('erp_runtime', '{table}', 'INSERT'),
                       has_table_privilege('erp_runtime', '{table}', 'UPDATE'),
                       has_table_privilege('erp_runtime', '{table}', 'DELETE')
                """, connection);
            await using var privileges = await grants.ExecuteReaderAsync(Ct);
            Assert.True(await privileges.ReadAsync(Ct));
            Assert.True(privileges.GetBoolean(0), $"{table} debe permitir SELECT al runtime.");
            Assert.True(privileges.GetBoolean(1), $"{table} debe permitir INSERT al runtime.");
            Assert.False(privileges.GetBoolean(2), $"{table} no debe permitir UPDATE al runtime.");
            Assert.False(privileges.GetBoolean(3), $"{table} no debe permitir DELETE al runtime.");
        }
    }

    [Fact]
    public async Task La_FK_compuesta_impide_una_linea_con_cuenta_de_otra_empresa()
    {
        await using var connection = await OpenAppAsync();
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await SetTenantAsync(connection, transaction, B);
        await using var foreign = new NpgsqlCommand("""
            INSERT INTO accounting.accounts
                (id, tenant_id, code, name, kind, normal_balance, parent_id, allows_posting, is_active, created_at_utc)
            VALUES (gen_random_uuid(), @tenant, @code, 'Cuenta ajena', 'Asset', 'Debit', NULL, true, true, now())
            RETURNING id
            """, connection, transaction);
        foreign.Parameters.AddWithValue("tenant", B);
        foreign.Parameters.AddWithValue("code", "FK-" + Guid.NewGuid().ToString("N").ToUpperInvariant());
        var foreignAccount = (Guid)(await foreign.ExecuteScalarAsync(Ct))!;

        await SetTenantAsync(connection, transaction, A);
        await using var entry = new NpgsqlCommand("""
            INSERT INTO accounting.journal_entries
                (id, tenant_id, accounting_date, description, idempotency_key, source_module,
                 source_document_type, source_document_id, status, posted_at_utc)
            VALUES (gen_random_uuid(), @tenant, '2026-09-09', 'Asiento con cuenta ajena', @key,
                    'sales', 'invoice', gen_random_uuid(), 0, now())
            RETURNING id
            """, connection, transaction);
        entry.Parameters.AddWithValue("tenant", A);
        entry.Parameters.AddWithValue("key", "fk:" + Guid.NewGuid().ToString("N"));
        var entryId = (Guid)(await entry.ExecuteScalarAsync(Ct))!;

        await using var line = new NpgsqlCommand("""
            INSERT INTO accounting.journal_entry_lines
                (id, tenant_id, journal_entry_id, line_number, account_id, account_code, debit, credit, memo)
            VALUES (gen_random_uuid(), @tenant, @entry, 1, @account, 'FK-AJENA', 1, 0, NULL)
            """, connection, transaction);
        line.Parameters.AddWithValue("tenant", A);
        line.Parameters.AddWithValue("entry", entryId);
        line.Parameters.AddWithValue("account", foreignAccount);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => line.ExecuteNonQueryAsync(Ct));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Theory]
    [InlineData(5, 5, 1)]
    [InlineData(0, 0, 1)]
    [InlineData(-1, 0, 1)]
    [InlineData(1, 0, 0)]
    public async Task Las_restricciones_de_la_linea_se_comprueban_en_la_base(decimal debit, decimal credit, int lineNumber)
    {
        await using var connection = await OpenAppAsync();
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await SetTenantAsync(connection, transaction, A);
        await using var account = new NpgsqlCommand("""
            INSERT INTO accounting.accounts
                (id, tenant_id, code, name, kind, normal_balance, parent_id, allows_posting, is_active, created_at_utc)
            VALUES (gen_random_uuid(), @tenant, @code, 'Cuenta de restriccion', 'Asset', 'Debit', NULL, true, true, now())
            RETURNING id
            """, connection, transaction);
        account.Parameters.AddWithValue("tenant", A);
        account.Parameters.AddWithValue("code", "CK-" + Guid.NewGuid().ToString("N").ToUpperInvariant());
        var accountId = (Guid)(await account.ExecuteScalarAsync(Ct))!;

        await using var entry = new NpgsqlCommand("""
            INSERT INTO accounting.journal_entries
                (id, tenant_id, accounting_date, description, idempotency_key, source_module,
                 source_document_type, source_document_id, status, posted_at_utc)
            VALUES (gen_random_uuid(), @tenant, '2026-09-09', 'Asiento de restriccion', @key,
                    'sales', 'invoice', gen_random_uuid(), 0, now())
            RETURNING id
            """, connection, transaction);
        entry.Parameters.AddWithValue("tenant", A);
        entry.Parameters.AddWithValue("key", "ck:" + Guid.NewGuid().ToString("N"));
        var entryId = (Guid)(await entry.ExecuteScalarAsync(Ct))!;

        await using var line = new NpgsqlCommand("""
            INSERT INTO accounting.journal_entry_lines
                (id, tenant_id, journal_entry_id, line_number, account_id, account_code, debit, credit, memo)
            VALUES (gen_random_uuid(), @tenant, @entry, @number, @account, 'CK', @debit, @credit, NULL)
            """, connection, transaction);
        line.Parameters.AddWithValue("tenant", A);
        line.Parameters.AddWithValue("entry", entryId);
        line.Parameters.AddWithValue("number", lineNumber);
        line.Parameters.AddWithValue("account", accountId);
        line.Parameters.AddWithValue("debit", debit);
        line.Parameters.AddWithValue("credit", credit);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => line.ExecuteNonQueryAsync(Ct));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }
}
