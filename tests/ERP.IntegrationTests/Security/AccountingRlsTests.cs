using ERP.DbMigrator;
using ERP.IntegrationTests.Infrastructure;
using Npgsql;

namespace ERP.IntegrationTests.Security;

[Collection(PostgresCollection.Name)]
public sealed class AccountingRlsTests(PostgresFixture db)
{
    private static readonly Guid A = DevelopmentSeeder.TenantAndinaId;
    private static readonly Guid B = DevelopmentSeeder.TenantPacificoId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
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

    private static async Task<Guid> InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenant, Guid? parent = null)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO accounting.accounts
                (id, tenant_id, code, name, kind, normal_balance, parent_id, allows_posting, is_active, created_at_utc)
            VALUES (gen_random_uuid(), @tenant, @code, 'Cuenta ficticia RLS', 'Asset', 'Debit', @parent, true, true, now())
            RETURNING id
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("code", "RLS-" + Guid.NewGuid().ToString("N").ToUpperInvariant());
        command.Parameters.AddWithValue("parent", NpgsqlTypes.NpgsqlDbType.Uuid, parent is { } id ? id : DBNull.Value);
        return (Guid)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task Tabla_tiene_RLS_forzado_y_rol_runtime_sin_bypass()
    {
        await using var connection = await OpenAsync();
        await using var table = new NpgsqlCommand("""
            SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = 'accounting.accounts'::regclass
            """, connection);
        await using (var reader = await table.ExecuteReaderAsync(Ct))
        {
            Assert.True(await reader.ReadAsync(Ct));
            Assert.True(reader.GetBoolean(0));
            Assert.True(reader.GetBoolean(1));
        }

        await using var role = new NpgsqlCommand("SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user", connection);
        Assert.Equal(false, await role.ExecuteScalarAsync(Ct));
    }

    [Fact]
    public async Task Sin_contexto_no_hay_filas_y_el_contexto_no_sobrevive_al_commit()
    {
        await using var connection = await OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync(Ct))
        {
            await SetTenantAsync(connection, transaction, A);
            var id = await InsertAsync(connection, transaction, A);
            await using var own = new NpgsqlCommand("SELECT count(*) FROM accounting.accounts WHERE id = @id", connection, transaction);
            own.Parameters.AddWithValue("id", id);
            Assert.Equal(1L, await own.ExecuteScalarAsync(Ct));
            await transaction.CommitAsync(Ct);
        }

        await using var noContext = new NpgsqlCommand("SELECT count(*) FROM accounting.accounts", connection);
        Assert.Equal(0L, await noContext.ExecuteScalarAsync(Ct));
        await using var next = await connection.BeginTransactionAsync(Ct);
        await SetTenantAsync(connection, next, B);
        await using var foreign = new NpgsqlCommand("SELECT count(*) FROM accounting.accounts WHERE tenant_id = @tenant", connection, next);
        foreign.Parameters.AddWithValue("tenant", A);
        Assert.Equal(0L, await foreign.ExecuteScalarAsync(Ct));
    }

    [Fact]
    public async Task RLS_impide_insertar_una_cuenta_de_otra_empresa()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await SetTenantAsync(connection, transaction, A);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(connection, transaction, B));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task RLS_oculta_e_impide_update_delete_de_cuentas_ajenas()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await SetTenantAsync(connection, transaction, B);
        var foreignId = await InsertAsync(connection, transaction, B);
        await SetTenantAsync(connection, transaction, A);
        foreach (var sql in new[]
        {
            "UPDATE accounting.accounts SET name = 'Intrusa' WHERE id = @id",
            "DELETE FROM accounting.accounts WHERE id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", foreignId);
            Assert.Equal(0, await command.ExecuteNonQueryAsync(Ct));
        }
    }

    [Fact]
    public async Task FK_compuesta_impide_padre_ajeno_incluso_con_contexto_de_hijo_correcto()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await SetTenantAsync(connection, transaction, A);
        var parentOfA = await InsertAsync(connection, transaction, A);
        await SetTenantAsync(connection, transaction, B);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(connection, transaction, B, parentOfA));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task With_check_impide_mover_una_cuenta_a_otra_empresa()
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await SetTenantAsync(connection, transaction, A);
        var ownId = await InsertAsync(connection, transaction, A);
        await using var command = new NpgsqlCommand("UPDATE accounting.accounts SET tenant_id = @tenant WHERE id = @id", connection, transaction);
        command.Parameters.AddWithValue("tenant", B);
        command.Parameters.AddWithValue("id", ownId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }
}
