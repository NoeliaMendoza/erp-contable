using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.DbMigrator;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Sales.Dtos;

namespace ERP.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class CustomersApiTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly Guid Andina = DevelopmentSeeder.TenantAndinaId;
    private static readonly Guid Pacifico = DevelopmentSeeder.TenantPacificoId;
    private ErpApiFactory _api = null!;
    private HttpClient _ana = null!;
    private HttpClient _bruno = null!;
    private HttpClient _carlaPacifico = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _api = new ErpApiFactory(db);
        _ana = _api.ClientFor(DevelopmentSeeder.AnaEmail, Andina);
        _bruno = _api.ClientFor(DevelopmentSeeder.BrunoEmail, Pacifico);
        _carlaPacifico = _api.ClientFor(DevelopmentSeeder.CarlaEmail, Pacifico);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Cliente_se_crea_paginado_editado_y_desactivado()
    {
        var number = Random.Shared.NextInt64(1_000_000_000, 10_000_000_000).ToString(CultureInfo.InvariantCulture);
        var created = await _ana.PostAsJsonAsync("/api/v1/customers",
            new SaveCustomerRequest("Cedula", number, "Cliente de prueba", "cliente@demo.local", "0991234567", "Quito"), Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var customer = (await created.Content.ReadFromJsonAsync<CustomerResponse>(Ct))!;
        Assert.True(customer.IsActive);

        var updated = await _ana.PutAsJsonAsync($"/api/v1/customers/{customer.Id}",
            new SaveCustomerRequest("Cedula", number, "Cliente actualizado", null, null, null), Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var status = await _ana.PatchAsJsonAsync($"/api/v1/customers/{customer.Id}",
            new ChangeCustomerStatusRequest(false), Ct);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.False((await status.Content.ReadFromJsonAsync<CustomerResponse>(Ct))!.IsActive);
    }

    [Fact]
    public async Task Duplicado_responde_409_y_el_mismo_documento_en_otro_tenant_se_permite()
    {
        var number = Random.Shared.NextInt64(1_000_000_000, 10_000_000_000).ToString(CultureInfo.InvariantCulture);
        var request = new SaveCustomerRequest("Cedula", number, "Cliente uno", null, null, null);
        var first = await _ana.PostAsJsonAsync("/api/v1/customers", request, Ct);
        var duplicate = await _ana.PostAsJsonAsync("/api/v1/customers", request, Ct);
        var other = await _bruno.PostAsJsonAsync("/api/v1/customers", request with { Name = "Cliente dos" }, Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_responde_401_y_rol_solo_consulta_responde_403_al_escribir()
    {
        var unauthenticated = await _api.ClientFor(null).GetAsync("/api/v1/customers", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var read = await _carlaPacifico.GetAsync("/api/v1/customers", Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var number = Random.Shared.NextInt64(1_000_000_000, 10_000_000_000).ToString(CultureInfo.InvariantCulture);
        var write = await _carlaPacifico.PostAsJsonAsync("/api/v1/customers",
            new SaveCustomerRequest("Pasaporte", "P-" + number, "No permitido", null, null, null), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Id_de_cliente_de_otra_empresa_responde_404()
    {
        var number = Random.Shared.NextInt64(1_000_000_000, 10_000_000_000).ToString(CultureInfo.InvariantCulture);
        var created = await _bruno.PostAsJsonAsync("/api/v1/customers",
            new SaveCustomerRequest("Cedula", number, "Cliente Pacífico", null, null, null), Ct);
        var customer = (await created.Content.ReadFromJsonAsync<CustomerResponse>(Ct))!;

        var foreign = await _ana.GetAsync($"/api/v1/customers/{customer.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }
}
