using ERP.DbMigrator;
using ERP.IntegrationTests.Infrastructure;
using Npgsql;

namespace ERP.IntegrationTests.Security;

/// <summary>
/// RLS probado con el ROL RUNTIME REAL (erp_app) mediante SQL directo, sin pasar por EF ni la API.
/// Simula a alguien que obtuvo la conexión de la aplicación e intenta saltarse los filtros.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RowLevelSecurityTests(PostgresFixture db)
{
    private static readonly Guid A = DevelopmentSeeder.TenantAndinaId;
    private static readonly Guid B = DevelopmentSeeder.TenantPacificoId;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task El_rol_runtime_no_es_superusuario_ni_puede_eludir_RLS()
    {
        await using var conn = await OpenAsync(db.AppConnectionString);
        await using var cmd = new NpgsqlCommand("SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        Assert.False(reader.GetBoolean(0));
        Assert.False(reader.GetBoolean(1));
    }

    [Theory]
    [InlineData("branches")]
    [InlineData("taxpayers")]
    [InlineData("roles")]
    [InlineData("tenants")]
    [InlineData("tenant_memberships")]
    [InlineData("establishments")]
    public async Task Sin_contexto_de_tenant_no_se_ve_ninguna_fila(string table)
    {
        await using var conn = await OpenAsync(db.AppConnectionString);
        Assert.Equal(0L, await ScalarAsync<long>(conn, null, $"SELECT count(*) FROM platform.{table}"));
    }

    [Fact]
    public async Task Con_contexto_A_solo_se_ven_filas_de_A()
    {
        await using var conn = await OpenAsync(db.AppConnectionString);
        await using var tx = await conn.BeginTransactionAsync(Ct);
        await SetTenantAsync(conn, tx, A);

        var tenantsInBranches = await ScalarAsync<long>(conn, tx, "SELECT count(DISTINCT tenant_id) FROM platform.branches");
        var foreign = await ScalarAsync<long>(conn, tx, $"SELECT count(*) FROM platform.branches WHERE tenant_id = '{B}'");

        Assert.Equal(1L, tenantsInBranches);
        Assert.Equal(0L, foreign);
    }

    [Fact]
    public async Task No_se_puede_insertar_en_otro_tenant()
    {
        await using var conn = await OpenAsync(db.AppConnectionString);
        await using var tx = await conn.BeginTransactionAsync(Ct);
        await SetTenantAsync(conn, tx, A);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync<int>(conn, tx, $"""
            INSERT INTO platform.branches (id, tenant_id, code, name, is_active, created_at_utc)
            VALUES (gen_random_uuid(), '{B}', 'HACK', 'Intrusa', true, now()) RETURNING 1
            """));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task No_se_puede_modificar_ni_borrar_filas_de_otro_tenant()
    {
        await using var conn = await OpenAsync(db.AppConnectionString);
        await using var tx = await conn.BeginTransactionAsync(Ct);
        await SetTenantAsync(conn, tx, A);

        Assert.Equal(0, await ExecuteAsync(conn, tx, $"UPDATE platform.branches SET name = 'x' WHERE tenant_id = '{B}'"));
        Assert.Equal(0, await ExecuteAsync(conn, tx, $"DELETE FROM platform.taxpayers WHERE tenant_id = '{B}'"));
    }

    [Fact]
    public async Task El_contexto_es_local_a_la_transaccion_y_no_se_filtra_por_el_pool()
    {
        await using var conn = await OpenAsync(db.AppConnectionString);
        await using (var tx = await conn.BeginTransactionAsync(Ct))
        {
            await SetTenantAsync(conn, tx, A);
            Assert.True(await ScalarAsync<long>(conn, tx, "SELECT count(*) FROM platform.branches") > 0);
            await tx.CommitAsync(Ct);
        }

        // Misma conexión física, nueva transacción sin contexto: denegación por defecto.
        Assert.Equal(0L, await ScalarAsync<long>(conn, null, "SELECT count(*) FROM platform.branches"));
    }

    [Fact]
    public async Task El_rol_de_migraciones_tambien_esta_sujeto_a_RLS_forzado()
    {
        await using var conn = await OpenAsync(db.MigratorConnectionString);
        Assert.Equal(0L, await ScalarAsync<long>(conn, null, "SELECT count(*) FROM platform.branches"));
    }

    [Fact]
    public async Task El_rol_runtime_no_puede_alterar_el_esquema_ni_desactivar_RLS()
    {
        await using var conn = await OpenAsync(db.AppConnectionString);
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(conn, null, "ALTER TABLE platform.branches DISABLE ROW LEVEL SECURITY"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    private async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(Ct);
        return conn;
    }

    private async Task SetTenantAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenantId)
    {
        await using var cmd = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", conn, tx);
        cmd.Parameters.AddWithValue("t", tenantId.ToString());
        await cmd.ExecuteScalarAsync(Ct);
    }

    private async Task<T> ScalarAsync<T>(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        return (T)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private async Task<int> ExecuteAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        return await cmd.ExecuteNonQueryAsync(Ct);
    }
}
