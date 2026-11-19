using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.BuildingBlocks.Paging;
using ERP.DbMigrator;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Accounting.Dtos;
using Npgsql;

namespace ERP.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class AccountingApiTests(PostgresFixture db) : IAsyncLifetime
{
    private ErpApiFactory _api = null!;
    private HttpClient _ana = null!;
    private HttpClient _bruno = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SaveAccountRequest Request(string? code = null, Guid? parent = null) =>
        new(code ?? "A-" + Guid.NewGuid().ToString("N"), "Cuenta de prueba", "Asset", "Debit", parent, true);

    public ValueTask InitializeAsync()
    {
        _api = new ErpApiFactory(db);
        _ana = _api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);
        _bruno = _api.ClientFor(DevelopmentSeeder.BrunoEmail, DevelopmentSeeder.TenantPacificoId);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _ana.Dispose();
        _bruno.Dispose();
        await _api.DisposeAsync();
    }

    private static async Task<AccountResponse> CreateAsync(HttpClient client, SaveAccountRequest? request = null)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/accounts", request ?? Request(), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(Ct))!;
    }

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Codigo_duplicado_en_la_misma_empresa_responde_409_incluso_inactivo()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        var request = Request();
        var account = await CreateAsync(_ana, request);
        using var status = await _ana.PatchAsJsonAsync($"/api/v1/accounts/{account.Id}/status", new { isActive = false }, Ct);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);

        using var duplicate = await _ana.PostAsJsonAsync("/api/v1/accounts", request with { Code = " " + request.Code.ToLowerInvariant() + " " }, Ct);
        await AssertCodeAsync(duplicate, HttpStatusCode.Conflict, "account_code_duplicated");
    }

    [Fact]
    public async Task Mismo_codigo_en_empresas_distintas_esta_permitido()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        var request = Request();
        var a = await CreateAsync(_ana, request);
        var b = await CreateAsync(_bruno, request);

        Assert.Equal(a.Code, b.Code);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public async Task Padre_de_otra_empresa_se_rechaza_en_creacion_y_edicion()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        var foreign = await CreateAsync(_bruno);
        using var create = await _ana.PostAsJsonAsync("/api/v1/accounts", Request(parent: foreign.Id), Ct);
        await AssertCodeAsync(create, HttpStatusCode.UnprocessableEntity, "account_parent_not_available");

        var own = await CreateAsync(_ana);
        using var update = await _ana.PutAsJsonAsync($"/api/v1/accounts/{own.Id}", Request(own.Code, foreign.Id), Ct);
        await AssertCodeAsync(update, HttpStatusCode.UnprocessableEntity, "account_parent_not_available");
    }

    [Fact]
    public async Task Ciclos_directos_e_indirectos_se_impiden()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        var a = await CreateAsync(_ana);
        var b = await CreateAsync(_ana, Request(parent: a.Id));
        var c = await CreateAsync(_ana, Request(parent: b.Id));

        using var direct = await _ana.PutAsJsonAsync($"/api/v1/accounts/{a.Id}", Request(a.Code, a.Id), Ct);
        await AssertCodeAsync(direct, HttpStatusCode.UnprocessableEntity, "account_hierarchy_cycle");
        using var indirect = await _ana.PutAsJsonAsync($"/api/v1/accounts/{a.Id}", Request(a.Code, c.Id), Ct);
        await AssertCodeAsync(indirect, HttpStatusCode.UnprocessableEntity, "account_hierarchy_cycle");
        var original = await _ana.GetFromJsonAsync<AccountResponse>($"/api/v1/accounts/{a.Id}", Ct);
        Assert.Null(original!.ParentId);
    }

    [Fact]
    public async Task Ediciones_simultaneas_no_forman_un_ciclo()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        var a = await CreateAsync(_ana);
        var b = await CreateAsync(_ana);

        var responses = await Task.WhenAll(
            _ana.PutAsJsonAsync($"/api/v1/accounts/{a.Id}", Request(a.Code, b.Id), Ct),
            _ana.PutAsJsonAsync($"/api/v1/accounts/{b.Id}", Request(b.Code, a.Id), Ct));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
            var currentA = await _ana.GetFromJsonAsync<AccountResponse>($"/api/v1/accounts/{a.Id}", Ct);
            var currentB = await _ana.GetFromJsonAsync<AccountResponse>($"/api/v1/accounts/{b.Id}", Ct);
            Assert.False(currentA!.ParentId == b.Id && currentB!.ParentId == a.Id);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Lectura_y_escritura_cruzadas_responden_404_y_no_alteran_la_cuenta()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        var foreign = await CreateAsync(_bruno);

        using var get = await _ana.GetAsync($"/api/v1/accounts/{foreign.Id}", Ct);
        using var put = await _ana.PutAsJsonAsync($"/api/v1/accounts/{foreign.Id}", Request(foreign.Code), Ct);
        using var patch = await _ana.PatchAsJsonAsync($"/api/v1/accounts/{foreign.Id}/status", new { isActive = false }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        var page = await _ana.GetFromJsonAsync<PagedResult<AccountResponse>>($"/api/v1/accounts?search={foreign.Code}", Ct);
        Assert.Empty(page!.Items);
        var unchanged = await _bruno.GetFromJsonAsync<AccountResponse>($"/api/v1/accounts/{foreign.Id}", Ct);
        Assert.True(unchanged!.IsActive);
        Assert.Equal(foreign.Name, unchanged.Name);
    }

    [Fact]
    public async Task Crear_editar_listar_desactivar_reactivar_y_auditoria_funcionan()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        var parent = await CreateAsync(_ana);
        var request = Request("00-" + Guid.NewGuid().ToString("N"), parent.Id);
        var account = await CreateAsync(_ana, request);
        Assert.Equal(parent.Id, account.ParentId);

        using var edit = await _ana.PutAsJsonAsync($"/api/v1/accounts/{account.Id}",
            request with { Name = "Bancos editada", Kind = "Liability", NormalBalance = "Credit", AllowsPosting = false }, Ct);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var updated = (await edit.Content.ReadFromJsonAsync<AccountResponse>(Ct))!;
        Assert.Equal("Bancos editada", updated.Name);
        Assert.Equal("Liability", updated.Kind);
        Assert.Equal("Credit", updated.NormalBalance);
        Assert.False(updated.AllowsPosting);

        using var deactivate = await _ana.PatchAsJsonAsync($"/api/v1/accounts/{account.Id}/status", new { isActive = false }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var retained = await _ana.GetFromJsonAsync<AccountResponse>($"/api/v1/accounts/{account.Id}", Ct);
        Assert.False(retained!.IsActive);
        var active = await _ana.GetFromJsonAsync<PagedResult<AccountResponse>>($"/api/v1/accounts?isActive=true&search={account.Code}", Ct);
        Assert.Empty(active!.Items);
        var inactive = await _ana.GetFromJsonAsync<PagedResult<AccountResponse>>($"/api/v1/accounts?isActive=false&search={account.Code}", Ct);
        Assert.Equal(account.Id, Assert.Single(inactive!.Items).Id);

        using var reactivate = await _ana.PatchAsJsonAsync($"/api/v1/accounts/{account.Id}/status", new { isActive = true }, Ct);
        Assert.True((await reactivate.Content.ReadFromJsonAsync<AccountResponse>(Ct))!.IsActive);

        await using var connection = new NpgsqlConnection(db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using var context = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction);
        context.Parameters.AddWithValue("tenant", DevelopmentSeeder.TenantAndinaId.ToString());
        await context.ExecuteNonQueryAsync(Ct);
        await using var audit = new NpgsqlCommand("SELECT created_by, updated_by, updated_at_utc >= created_at_utc FROM accounting.accounts WHERE id = @id", connection, transaction);
        audit.Parameters.AddWithValue("id", account.Id);
        await using var reader = await audit.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        Assert.Equal(DevelopmentSeeder.AnaId, reader.GetGuid(0));
        Assert.Equal(DevelopmentSeeder.AnaId, reader.GetGuid(1));
        Assert.True(reader.GetBoolean(2));
    }

    [Fact]
    public async Task Edicion_no_permite_duplicar_codigo_y_el_nombre_no_debe_cambiar()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        var a = await CreateAsync(_ana);
        var b = await CreateAsync(_ana);

        using var response = await _ana.PutAsJsonAsync($"/api/v1/accounts/{b.Id}", Request(a.Code) with { Name = "No guardar" }, Ct);
        await AssertCodeAsync(response, HttpStatusCode.Conflict, "account_code_duplicated");
        Assert.Equal(b, await _ana.GetFromJsonAsync<AccountResponse>($"/api/v1/accounts/{b.Id}", Ct));
    }

    [Fact]
    public async Task Validacion_y_estado_obligatorio_responden_400()
    {
        // Estas comprobaciones no acceden a accounting.accounts y no requieren su migración.
        foreach (var request in new[]
        {
            Request() with { Code = "con espacios" }, Request() with { Name = "" },
            Request() with { Kind = "1" }, Request() with { NormalBalance = "Otro" },
            Request() with { ParentId = Guid.Empty }, Request() with { Code = null! },
        })
        {
            using var response = await _ana.PostAsJsonAsync("/api/v1/accounts", request, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var emptyStatus = await _ana.PatchAsJsonAsync($"/api/v1/accounts/{Guid.NewGuid()}/status", new { }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, emptyStatus.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_sin_empresa_o_empresa_ajena_se_rechazan()
    {
        using var anonymous = _api.ClientFor(null);
        using var noTenant = _api.ClientFor(DevelopmentSeeder.AnaEmail);
        using var foreignTenant = _api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantPacificoId);
        using var unauthenticated = await anonymous.GetAsync("/api/v1/accounts", Ct);
        using var missingTenant = await noTenant.GetAsync("/api/v1/accounts", Ct);
        using var forbidden = await foreignTenant.GetAsync("/api/v1/accounts", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Paginacion_orden_y_busqueda_literal_respetan_el_contrato()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        var prefix = "PAGE-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await CreateAsync(_ana, Request(prefix + "-02"));
        await CreateAsync(_ana, Request(prefix + "-01"));

        var page = await _ana.GetFromJsonAsync<PagedResult<AccountResponse>>($"/api/v1/accounts?search={prefix}&page=1&pageSize=1", Ct);
        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(prefix + "-01", Assert.Single(page.Items).Code);
        var second = await _ana.GetFromJsonAsync<PagedResult<AccountResponse>>($"/api/v1/accounts?search={prefix}&page=2&pageSize=1", Ct);
        Assert.Equal(prefix + "-02", Assert.Single(second!.Items).Code);
        var adjusted = await _ana.GetFromJsonAsync<PagedResult<AccountResponse>>("/api/v1/accounts?page=0&pageSize=500&search=%25", Ct);
        Assert.Equal(1, adjusted!.Page);
        Assert.Equal(100, adjusted.PageSize);
        Assert.Empty(adjusted.Items);
    }

    [Fact]
    public async Task Permiso_de_lectura_no_autoriza_crear_editar_o_desactivar()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        // Preparar SOLO datos del contenedor de pruebas, sin cambiar el seeder compartido.
        await using var connection = new NpgsqlConnection(db.MigratorConnectionString);
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using var context = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction);
        context.Parameters.AddWithValue("tenant", DevelopmentSeeder.TenantPacificoId.ToString());
        await context.ExecuteNonQueryAsync(Ct);
        await using var grant = new NpgsqlCommand("""
            INSERT INTO platform.role_permissions (tenant_id, role_id, permission_code)
            SELECT tenant_id, id, 'accounting.accounts.read' FROM platform.roles
            WHERE name = 'Consulta'
            ON CONFLICT DO NOTHING
            """, connection, transaction);
        await grant.ExecuteNonQueryAsync(Ct);
        await transaction.CommitAsync(Ct);

        var account = await CreateAsync(_bruno);
        using var reader = _api.ClientFor(DevelopmentSeeder.CarlaEmail, DevelopmentSeeder.TenantPacificoId);
        using var get = await reader.GetAsync($"/api/v1/accounts/{account.Id}", Ct);
        using var list = await reader.GetAsync("/api/v1/accounts", Ct);
        using var create = await reader.PostAsJsonAsync("/api/v1/accounts", Request(), Ct);
        using var edit = await reader.PutAsJsonAsync($"/api/v1/accounts/{account.Id}", Request(account.Code), Ct);
        using var status = await reader.PatchAsJsonAsync($"/api/v1/accounts/{account.Id}/status", new { isActive = false }, Ct);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
    }
}
