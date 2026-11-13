using System.Net;
using System.Net.Http.Json;
using ERP.DbMigrator;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Platform.Dtos;

namespace ERP.IntegrationTests.Api;

/// <summary>
/// Flujo vertical real API → EF Core → PostgreSQL (RLS) con dos empresas ficticias:
/// Ana (admin de Andina), Bruno (admin de Pacífico), Carla (admin de Andina y solo consulta en Pacífico).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantIsolationApiTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly Guid Andina = DevelopmentSeeder.TenantAndinaId;
    private static readonly Guid Pacifico = DevelopmentSeeder.TenantPacificoId;

    private ErpApiFactory _api = null!;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _api = new ErpApiFactory(db);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Sin_autenticacion_responde_401()
    {
        var response = await _api.ClientFor(null).GetAsync("/api/v1/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_desconocido_responde_401()
    {
        var response = await _api.ClientFor("nadie@demo.local").GetAsync("/api/v1/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cada_usuario_ve_solo_sus_empresas()
    {
        var ana = await _api.ClientFor(DevelopmentSeeder.AnaEmail).GetFromJsonAsync<MyProfileResponse>("/api/v1/me", Ct);
        var carla = await _api.ClientFor(DevelopmentSeeder.CarlaEmail).GetFromJsonAsync<MyProfileResponse>("/api/v1/me", Ct);

        Assert.Equal([Andina], ana!.Memberships.Select(m => m.TenantId));
        Assert.Equal(2, carla!.Memberships.Count);
    }

    [Fact]
    public async Task Empresa_activa_devuelve_modulos_y_permisos_del_tenant()
    {
        var current = await _api.ClientFor(DevelopmentSeeder.AnaEmail, Andina)
            .GetFromJsonAsync<CurrentTenantResponse>("/api/v1/tenant/current", Ct);

        Assert.Equal(Andina, current!.TenantId);
        Assert.Contains("inventory", current.EnabledFeatures);
        Assert.DoesNotContain("purchases", current.EnabledFeatures);
        Assert.Contains("platform.branches.manage", current.MyPermissions);
    }

    [Fact]
    public async Task Seleccionar_una_empresa_ajena_responde_403()
    {
        var response = await _api.ClientFor(DevelopmentSeeder.AnaEmail, Pacifico).GetAsync("/api/v1/tenant/branches", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_de_empresa_sin_cabecera_responde_400()
    {
        var response = await _api.ClientFor(DevelopmentSeeder.AnaEmail).GetAsync("/api/v1/tenant/branches", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Listados_solo_contienen_datos_del_tenant_activo()
    {
        var andina = await _api.ClientFor(DevelopmentSeeder.AnaEmail, Andina)
            .GetFromJsonAsync<List<BranchResponse>>("/api/v1/tenant/branches", Ct);
        var pacifico = await _api.ClientFor(DevelopmentSeeder.BrunoEmail, Pacifico)
            .GetFromJsonAsync<List<BranchResponse>>("/api/v1/tenant/branches", Ct);

        Assert.Contains(andina!, b => b.Code == "MATRIZ");
        Assert.DoesNotContain(andina!, b => b.Code == "GYE");
        Assert.Contains(pacifico!, b => b.Code == "GYE");
        Assert.DoesNotContain(pacifico!, b => b.Code == "MATRIZ");
    }

    [Fact]
    public async Task Id_adivinado_de_otra_empresa_responde_404()
    {
        var pacificoBranches = await _api.ClientFor(DevelopmentSeeder.BrunoEmail, Pacifico)
            .GetFromJsonAsync<List<BranchResponse>>("/api/v1/tenant/branches", Ct);
        var foreignId = pacificoBranches!.First().Id;

        var response = await _api.ClientFor(DevelopmentSeeder.AnaEmail, Andina).GetAsync($"/api/v1/tenant/branches/{foreignId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rol_de_consulta_no_puede_crear_aunque_manipule_el_cliente()
    {
        var carlaEnPacifico = _api.ClientFor(DevelopmentSeeder.CarlaEmail, Pacifico);

        var read = await carlaEnPacifico.GetAsync("/api/v1/tenant/branches", Ct);
        var write = await carlaEnPacifico.PostAsJsonAsync("/api/v1/tenant/branches", new CreateBranchRequest("CARLA", "No permitida", null, null), Ct);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Sucursal_creada_queda_aislada_en_su_empresa()
    {
        var code = $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var created = await _api.ClientFor(DevelopmentSeeder.AnaEmail, Andina)
            .PostAsJsonAsync("/api/v1/tenant/branches", new CreateBranchRequest(code, "Sucursal de prueba", "Calle ficticia", "Quito"), Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var bruno = await _api.ClientFor(DevelopmentSeeder.BrunoEmail, Pacifico)
            .GetFromJsonAsync<List<BranchResponse>>("/api/v1/tenant/branches", Ct);
        Assert.DoesNotContain(bruno!, b => b.Code == code);
    }

    [Fact]
    public async Task Entrada_invalida_responde_400_con_errores()
    {
        var response = await _api.ClientFor(DevelopmentSeeder.AnaEmail, Andina)
            .PostAsJsonAsync("/api/v1/tenant/branches", new CreateBranchRequest("con espacios", "", null, null), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("errors", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Codigo_duplicado_responde_422()
    {
        var response = await _api.ClientFor(DevelopmentSeeder.AnaEmail, Andina)
            .PostAsJsonAsync("/api/v1/tenant/branches", new CreateBranchRequest("MATRIZ", "Duplicada", null, null), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Configuracion_fiscal_por_tenant_esta_aislada()
    {
        var andina = await _api.ClientFor(DevelopmentSeeder.AnaEmail, Andina)
            .GetFromJsonAsync<List<TaxpayerResponse>>("/api/v1/tenant/taxpayers", Ct);
        var pacifico = await _api.ClientFor(DevelopmentSeeder.BrunoEmail, Pacifico)
            .GetFromJsonAsync<List<TaxpayerResponse>>("/api/v1/tenant/taxpayers", Ct);

        var a = Assert.Single(andina!);
        var p = Assert.Single(pacifico!);
        Assert.Equal("1799999990001", a.Ruc);
        Assert.Equal("SociedadPrivada", a.Kind);
        Assert.True(a.KeepsAccounting);
        Assert.Equal("001", a.Establishments.Single().EmissionPoints.Single().Code);
        Assert.Equal("0102030400001", p.Ruc);
        Assert.Equal("PersonaNatural", p.Kind);
    }

    [Fact]
    public async Task Health_ready_verifica_PostgreSQL()
    {
        var response = await _api.ClientFor(null).GetAsync("/health/ready", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
