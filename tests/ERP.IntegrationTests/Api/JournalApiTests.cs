using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.BuildingBlocks.Paging;
using ERP.DbMigrator;
using ERP.Infrastructure.Tenancy;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Accounting.Contracts;
using ERP.Modules.Accounting.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ERP.IntegrationTests.Api;

/// <summary>ACC-03: libro diario por empresa, de solo lectura y con filtro por fecha contable.</summary>
[Collection(PostgresCollection.Name)]
public sealed class JournalApiTests(PostgresFixture db) : IAsyncLifetime
{
    private ErpApiFactory _api = null!;
    private HttpClient _ana = null!;
    private HttpClient _bruno = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        _api = new ErpApiFactory(db);
        _ana = _api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);
        _bruno = _api.ClientFor(DevelopmentSeeder.BrunoEmail, DevelopmentSeeder.TenantPacificoId);
    }

    public async ValueTask DisposeAsync()
    {
        _ana.Dispose();
        _bruno.Dispose();
        await _api.DisposeAsync();
    }

    private async Task<string> AccountAsync(HttpClient client)
    {
        var code = "J" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        using var response = await client.PostAsJsonAsync("/api/v1/accounts",
            new SaveAccountRequest(code, "Cuenta de diario", "Asset", "Debit", null, true), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(Ct))!.Code;
    }

    private async Task<Guid> PostAsync(Guid tenantId, string debit, string credit, DateOnly date, string description)
    {
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantSession>().ConfirmTenant(tenantId, []);
        var service = scope.ServiceProvider.GetRequiredService<IAccountingPostingService>();
        var result = await service.PostAsync(new PostingRequest("diario:" + Guid.NewGuid().ToString("N"),
            "sales", "invoice", Guid.CreateVersion7(), date, description,
            [new(debit, 20m, 0m, "debe"), new(credit, 0m, 20m, null)]), Ct);
        return result.JournalEntryId;
    }

    private Task<PagedResult<JournalEntryResponse>?> ListAsync(HttpClient client, string query = "") =>
        client.GetFromJsonAsync<PagedResult<JournalEntryResponse>>("/api/v1/journal-entries" + query, Ct);

    [Fact]
    public async Task El_filtro_de_fechas_usa_la_fecha_contable_y_ordena_del_mas_reciente_al_mas_antiguo()
    {
        var debit = await AccountAsync(_ana);
        var credit = await AccountAsync(_ana);
        var marca = "Rango-" + Guid.NewGuid().ToString("N")[..8];
        var antiguo = await PostAsync(DevelopmentSeeder.TenantAndinaId, debit, credit, new DateOnly(2026, 1, 10), marca + " antiguo");
        var medio = await PostAsync(DevelopmentSeeder.TenantAndinaId, debit, credit, new DateOnly(2026, 2, 10), marca + " medio");
        var nuevo = await PostAsync(DevelopmentSeeder.TenantAndinaId, debit, credit, new DateOnly(2026, 3, 10), marca + " nuevo");

        var rango = await ListAsync(_ana, "?from=2026-02-01&to=2026-03-01&pageSize=100");
        Assert.DoesNotContain(antiguo, rango!.Items.Select(e => e.Id));
        Assert.DoesNotContain(nuevo, rango.Items.Select(e => e.Id));
        Assert.Contains(medio, rango.Items.Select(e => e.Id));

        var desde = await ListAsync(_ana, "?from=2026-02-10&pageSize=100");
        Assert.DoesNotContain(antiguo, desde!.Items.Select(e => e.Id));
        var hasta = await ListAsync(_ana, "?to=2026-02-10&pageSize=100");
        Assert.DoesNotContain(nuevo, hasta!.Items.Select(e => e.Id));

        // Los tres asientos de esta prueba deben aparecer del más reciente al más antiguo.
        var todos = (await ListAsync(_ana, "?pageSize=100"))!.Items
            .Where(e => e.Description.StartsWith(marca, StringComparison.Ordinal)).Select(e => e.Id).ToArray();
        Assert.Equal([nuevo, medio, antiguo], todos);
    }

    [Fact]
    public async Task Un_rango_invertido_responde_422_con_codigo_estable()
    {
        using var response = await _ana.GetAsync("/api/v1/journal-entries?from=2026-05-01&to=2026-04-01", Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("journal_date_range_invalid", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task La_paginacion_se_ajusta_y_el_detalle_incluye_cuenta_y_totales()
    {
        var debit = await AccountAsync(_ana);
        var credit = await AccountAsync(_ana);
        var fecha = new DateOnly(2026, 6, 1);
        var primero = await PostAsync(DevelopmentSeeder.TenantAndinaId, debit, credit, fecha, "Paginado uno");
        var segundo = await PostAsync(DevelopmentSeeder.TenantAndinaId, debit, credit, fecha, "Paginado dos");

        var page = await ListAsync(_ana, "?from=2026-06-01&to=2026-06-01&page=1&pageSize=1");
        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(1, page.Page);
        var single = Assert.Single(page.Items);
        var second = await ListAsync(_ana, "?from=2026-06-01&to=2026-06-01&page=2&pageSize=1");
        var other = Assert.Single(second!.Items);
        // Las dos páginas de tamaño 1 devuelven exactamente los dos asientos creados, sin repetir ninguno.
        Assert.Equal(2, new HashSet<Guid> { single.Id, other.Id, primero, segundo }.Count);

        var ajustada = await ListAsync(_ana, "?page=0&pageSize=500");
        Assert.Equal(1, ajustada!.Page);
        Assert.Equal(100, ajustada.PageSize);

        var detalle = await _ana.GetFromJsonAsync<JournalEntryResponse>($"/api/v1/journal-entries/{primero}", Ct);
        Assert.Equal(20m, detalle!.TotalDebit);
        Assert.Equal(20m, detalle.TotalCredit);
        Assert.Equal(2, detalle.Lines.Count);
        Assert.Equal(debit, detalle.Lines[0].AccountCode);
        Assert.Equal("Cuenta de diario", detalle.Lines[0].AccountName);
        Assert.All(detalle.Lines, l => Assert.NotEqual(Guid.Empty, l.AccountId));
    }

    [Fact]
    public async Task Cada_empresa_solo_ve_su_libro_diario()
    {
        var debit = await AccountAsync(_bruno);
        var credit = await AccountAsync(_bruno);
        var ajeno = await PostAsync(DevelopmentSeeder.TenantPacificoId, debit, credit, new DateOnly(2026, 7, 7), "Solo Pacifico");

        var propio = await ListAsync(_bruno, "?from=2026-07-07&to=2026-07-07&pageSize=100");
        Assert.Contains(ajeno, propio!.Items.Select(e => e.Id));
        var otro = await ListAsync(_ana, "?from=2026-07-07&to=2026-07-07&pageSize=100");
        Assert.DoesNotContain(ajeno, otro!.Items.Select(e => e.Id));
        using var cruzado = await _ana.GetAsync($"/api/v1/journal-entries/{ajeno}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, cruzado.StatusCode);
    }

    [Fact]
    public async Task Un_asiento_inexistente_responde_404()
    {
        using var response = await _ana.GetAsync($"/api/v1/journal-entries/{Guid.CreateVersion7()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_sin_empresa_o_sin_permiso_el_libro_no_se_expone()
    {
        using var anonymous = _api.ClientFor(null);
        using var noTenant = _api.ClientFor(DevelopmentSeeder.AnaEmail);
        using var sinPermiso = _api.ClientFor(DevelopmentSeeder.CarlaEmail, DevelopmentSeeder.TenantPacificoId);

        using var unauthenticated = await anonymous.GetAsync("/api/v1/journal-entries", Ct);
        using var missingTenant = await noTenant.GetAsync("/api/v1/journal-entries", Ct);
        using var forbidden = await sinPermiso.GetAsync("/api/v1/journal-entries", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task El_libro_no_admite_escrituras_por_HTTP()
    {
        var debit = await AccountAsync(_ana);
        var credit = await AccountAsync(_ana);
        var id = await PostAsync(DevelopmentSeeder.TenantAndinaId, debit, credit, new DateOnly(2026, 8, 8), "Inmutable");

        using var post = await _ana.PostAsJsonAsync("/api/v1/journal-entries", new { description = "x" }, Ct);
        using var put = await _ana.PutAsJsonAsync($"/api/v1/journal-entries/{id}", new { description = "x" }, Ct);
        using var delete = await _ana.DeleteAsync($"/api/v1/journal-entries/{id}", Ct);

        foreach (var response in new[] { post, put, delete })
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        var unchanged = await _ana.GetFromJsonAsync<JournalEntryResponse>($"/api/v1/journal-entries/{id}", Ct);
        Assert.Equal("Inmutable", unchanged!.Description);
    }
}
