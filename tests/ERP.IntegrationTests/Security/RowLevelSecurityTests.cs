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
    [InlineData("platform.branches")]
    [InlineData("platform.taxpayers")]
    [InlineData("platform.roles")]
    [InlineData("platform.tenants")]
    [InlineData("platform.tenant_memberships")]
    [InlineData("platform.establishments")]
    [InlineData("inventory.units")]
    [InlineData("inventory.products")]
    [InlineData("inventory.categories")]
    public async Task Sin_contexto_de_tenant_no_se_ve_ninguna_fila(string table)
    {
        await using var conn = await OpenAsync(db.AppConnectionString);
        Assert.Equal(0L, await ScalarAsync<long>(conn, null, $"SELECT count(*) FROM {table}"));
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
    public async Task La_base_impide_un_producto_con_unidad_de_otra_empresa()
    {
        await using var conn = await OpenAsync(db.AppConnectionString);
        await using var tx = await conn.BeginTransactionAsync(Ct);
        await SetTenantAsync(conn, tx, A);
        var unitOfA = await ScalarAsync<Guid>(conn, tx, "SELECT id FROM inventory.units LIMIT 1");

        // Ni siquiera con el contexto correcto se puede apuntar a la unidad de A desde una fila de B:
        // RLS rechaza tenant_id = B y la FK compuesta (tenant_id, unit_id) impediría el vínculo cruzado.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync<int>(conn, tx, $"""
            INSERT INTO inventory.products (id, tenant_id, sku, name, kind, unit_id, price, vat_treatment, tracks_inventory, is_active, created_at_utc)
            VALUES (gen_random_uuid(), '{B}', 'HACK', 'Intruso', 'Product', '{unitOfA}', 1, 'Standard', true, true, now()) RETURNING 1
            """));

        Assert.Contains(ex.SqlState, new[] { PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.ForeignKeyViolation });
    }

    [Fact]
    public async Task La_base_impide_asignar_la_categoria_de_otra_empresa()
    {
        await using var conn = await OpenAsync(db.AppConnectionString);
        await using var tx = await conn.BeginTransactionAsync(Ct);

        // Categoría de A creada con el contexto de A.
        await SetTenantAsync(conn, tx, A);
        var categoryOfA = await ScalarAsync<Guid>(conn, tx, $"""
            INSERT INTO inventory.categories (id, tenant_id, name, is_active, created_at_utc)
            VALUES (gen_random_uuid(), '{A}', 'Cat-' || gen_random_uuid(), true, now()) RETURNING id
            """);

        // Con el contexto de B, un producto de B no puede apuntar a esa categoría: la FK compuesta
        // (tenant_id, category_id) solo encuentra categorías de B.
        await SetTenantAsync(conn, tx, B);
        var unitOfB = await ScalarAsync<Guid>(conn, tx, $"""
            INSERT INTO inventory.units (id, tenant_id, code, name, is_active, created_at_utc)
            VALUES (gen_random_uuid(), '{B}', 'U' || substr(md5(random()::text), 1, 8), 'Unidad', true, now()) RETURNING id
            """);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync<int>(conn, tx, $"""
            INSERT INTO inventory.products (id, tenant_id, sku, name, kind, unit_id, price, vat_treatment, tracks_inventory, category_id, is_active, created_at_utc)
            VALUES (gen_random_uuid(), '{B}', 'CAT-HACK', 'Intruso', 'Product', '{unitOfB}', 1, 'Standard', true, '{categoryOfA}', true, now()) RETURNING 1
            """));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }

    [Fact]
    public async Task La_base_aplica_las_reglas_de_la_ficha_aunque_falle_el_codigo()
    {
        await using var conn = await OpenAsync(db.AppConnectionString);
        await using var tx = await conn.BeginTransactionAsync(Ct);
        await SetTenantAsync(conn, tx, A);
        var unitOfA = await ScalarAsync<Guid>(conn, tx, "SELECT id FROM inventory.units LIMIT 1");
        await ExecuteAsync(conn, tx, "SAVEPOINT s");

        string[] invalidValues =
        [
            "'Service', 'Standard', true, NULL",     // servicio con stock
            "'Product', 'Standard', false, 5",       // stock mínimo sin control
            "'Product', 'IVA12', true, NULL",        // IVA desconocido
        ];

        foreach (var values in invalidValues)
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(conn, tx, $"""
                INSERT INTO inventory.products (id, tenant_id, sku, name, unit_id, price, kind, vat_treatment, tracks_inventory, min_stock, is_active, created_at_utc)
                VALUES (gen_random_uuid(), '{A}', 'CK-' || substr(md5(random()::text), 1, 8), 'Regla', '{unitOfA}', 1, {values}, true, now())
                """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
            await ExecuteAsync(conn, tx, "ROLLBACK TO SAVEPOINT s");
        }
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
