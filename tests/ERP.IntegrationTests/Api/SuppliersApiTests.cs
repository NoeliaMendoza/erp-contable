using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.DbMigrator;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Purchases.Dtos;

namespace ERP.IntegrationTests.Api;

/// <summary>CRUD, permisos y aislamiento del catálogo de proveedores en PostgreSQL real.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SuppliersApiTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly Guid Andina = DevelopmentSeeder.TenantAndinaId;
    private static readonly Guid Pacifico = DevelopmentSeeder.TenantPacificoId;
    private ErpApiFactory _api = null!;
    private HttpClient _ana = null!;
    private HttpClient _bruno = null!;
    private HttpClient _carla = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _api = new ErpApiFactory(db);
        _ana = _api.ClientFor(DevelopmentSeeder.AnaEmail, Andina);
        _bruno = _api.ClientFor(DevelopmentSeeder.BrunoEmail, Pacifico);
        _carla = _api.ClientFor(DevelopmentSeeder.CarlaEmail, Pacifico);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task CRUD_busqueda_paginacion_y_baja_logica()
    {
        var request = ValidRequest("crud");
        var createdResponse = await _ana.PostAsJsonAsync("/api/v1/suppliers", request, Ct);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<SupplierResponse>(Ct))!;
        Assert.True(created.IsActive);

        var byId = await _ana.GetFromJsonAsync<SupplierResponse>($"/api/v1/suppliers/{created.Id}", Ct);
        Assert.Equal(created.Id, byId!.Id);

        var page = await _ana.GetFromJsonAsync<SupplierPage>("/api/v1/suppliers?search=CRUD&page=1&pageSize=1", Ct);
        Assert.Contains(page!.Items, s => s.Id == created.Id);
        var byIdentification = await _ana.GetFromJsonAsync<SupplierPage>(
            $"/api/v1/suppliers?search={Uri.EscapeDataString(created.IdentificationNumber)}", Ct);
        Assert.Contains(byIdentification!.Items, s => s.Id == created.Id);

        var update = await _ana.PutAsJsonAsync($"/api/v1/suppliers/{created.Id}", request with { Name = "Proveedor actualizado" }, Ct);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Proveedor actualizado", (await update.Content.ReadFromJsonAsync<SupplierResponse>(Ct))!.Name);

        var disabled = await _ana.PatchAsJsonAsync($"/api/v1/suppliers/{created.Id}/status", new ChangeSupplierStatusRequest(false), Ct);
        Assert.False((await disabled.Content.ReadFromJsonAsync<SupplierResponse>(Ct))!.IsActive);
        var inactive = await _ana.GetFromJsonAsync<SupplierPage>(
            $"/api/v1/suppliers?isActive=false&search={Uri.EscapeDataString(created.IdentificationNumber)}", Ct);
        Assert.Contains(inactive!.Items, s => s.Id == created.Id);
        var reactivated = await _ana.PatchAsJsonAsync($"/api/v1/suppliers/{created.Id}/status", new ChangeSupplierStatusRequest(true), Ct);
        Assert.True((await reactivated.Content.ReadFromJsonAsync<SupplierResponse>(Ct))!.IsActive);
    }

    [Fact]
    public async Task Duplicado_normalizado_en_la_misma_empresa_responde_409_y_otra_empresa_puede_reutilizarlo()
    {
        var request = ValidRequest("same-tenant");
        Assert.Equal(HttpStatusCode.Created, (await _ana.PostAsJsonAsync("/api/v1/suppliers", request, Ct)).StatusCode);
        var duplicate = await _ana.PostAsJsonAsync("/api/v1/suppliers", request with
        {
            IdentificationType = request.IdentificationType.ToLowerInvariant(),
            IdentificationNumber = "  " + request.IdentificationNumber.ToLowerInvariant() + "  "
        }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var otherTenant = await _bruno.PostAsJsonAsync("/api/v1/suppliers", request, Ct);
        Assert.Equal(HttpStatusCode.Created, otherTenant.StatusCode);
    }

    [Fact]
    public async Task Lectura_y_escritura_cruzada_no_revelan_proveedores_ajenos()
    {
        var created = await CreateAsync(_ana, "cross-tenant");
        Assert.Equal(HttpStatusCode.NotFound, (await _bruno.GetAsync($"/api/v1/suppliers/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _bruno.PutAsJsonAsync($"/api/v1/suppliers/{created.Id}", ValidRequest("cross-edit"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _bruno.PatchAsJsonAsync($"/api/v1/suppliers/{created.Id}/status", new ChangeSupplierStatusRequest(false), Ct)).StatusCode);
    }

    [Fact]
    public async Task Permiso_solo_lectura_bloquea_escrituras_y_cuerpo_no_puede_fijar_tenant()
    {
        var supplier = await CreateAsync(_bruno, "readonly");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _carla.PostAsJsonAsync("/api/v1/suppliers", ValidRequest("readonly-post"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _carla.PutAsJsonAsync($"/api/v1/suppliers/{supplier.Id}", ValidRequest("readonly-put"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _carla.PatchAsJsonAsync($"/api/v1/suppliers/{supplier.Id}/status", new ChangeSupplierStatusRequest(false), Ct)).StatusCode);

        using var body = JsonDocument.Parse(JsonSerializer.Serialize(ValidRequest("tenant-body")));
        var payload = JsonNode(body.RootElement, Pacifico);
        var response = await _ana.PostAsJsonAsync("/api/v1/suppliers", payload, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Proveedor TENANT-BODY", (await response.Content.ReadFromJsonAsync<SupplierResponse>(Ct))!.Name);
    }

    [Fact]
    public async Task Datos_invalidos_se_rechazan_y_sin_autenticacion_responde_401()
    {
        var invalid = ValidRequest("bad-email") with { Email = "no-es-email" };
        Assert.Equal(HttpStatusCode.BadRequest, (await _ana.PostAsJsonAsync("/api/v1/suppliers", invalid, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _ana.PostAsJsonAsync("/api/v1/suppliers", ValidRequest("empty") with { Name = " " }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _api.ClientFor(null, Andina).GetAsync("/api/v1/suppliers", Ct)).StatusCode);
    }

    [Fact]
    public async Task Catalogo_de_proveedores_excluye_consumidor_final_y_rechaza_tipo_07()
    {
        var types = await _ana.GetFromJsonAsync<SupplierIdentificationTypeResponse[]>(
            "/api/v1/suppliers/identification-types", Ct);
        Assert.Equal(["04", "05", "06", "08"], types!.Select(type => type.SriCode));

        var invalidSupplier = ValidRequest("final-consumer") with
        {
            IdentificationType = "07",
            IdentificationNumber = "9999999999999"
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _ana.PostAsJsonAsync("/api/v1/suppliers", invalidSupplier, Ct)).StatusCode);
    }

    [Fact]
    public async Task Solicitudes_concurrentes_no_crean_identificaciones_duplicadas()
    {
        var request = ValidRequest("parallel");
        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => _ana.PostAsJsonAsync("/api/v1/suppliers", request, Ct)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses) response.Dispose();
    }

    private static SaveSupplierRequest ValidRequest(string suffix) => new(
        "06", "ab-" + Guid.NewGuid().ToString("N")[..16], $"Proveedor {suffix.ToUpperInvariant()}",
        "proveedor@example.com", null, null);

    private static async Task<SupplierResponse> CreateAsync(HttpClient client, string suffix)
    {
        var response = await client.PostAsJsonAsync("/api/v1/suppliers", ValidRequest(suffix), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SupplierResponse>(Ct))!;
    }

    private static Dictionary<string, object?> JsonNode(JsonElement original, Guid tenantId)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(original.GetRawText())!;
        data["tenantId"] = tenantId;
        data["createdBy"] = Guid.NewGuid();
        data["isActive"] = false;
        return data;
    }

    private sealed record SupplierPage(IReadOnlyList<SupplierResponse> Items, int TotalCount, int Page, int PageSize);
}
