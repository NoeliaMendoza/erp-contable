using System.Net;
using System.Net.Http.Json;
using ERP.DbMigrator;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Platform.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ERP.IntegrationTests.Api;

/// <summary>
/// PLAT-01: inicio de sesión real (Identity + OpenIddict, Authorization Code + PKCE) contra PostgreSQL real y la API con
/// el rol runtime. Cubre login, cancelación, credenciales rechazadas, 401 (sin token, inválido, expirado), 403 (empresa
/// ajena, sin permiso) y aislamiento A/B usando tokens Bearer, sin la cabecera de desarrollo.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OidcAuthenticationApiTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly Guid Andina = DevelopmentSeeder.TenantAndinaId;
    private static readonly Guid Pacifico = DevelopmentSeeder.TenantPacificoId;

    private ErpApiFactory _api = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _api = new ErpApiFactory(db);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Login_real_con_PKCE_entrega_token_que_abre_un_endpoint_protegido()
    {
        var tokens = await new OidcTestClient(_api).LoginAsync(DevelopmentSeeder.AnaEmail, db.DemoPassword, Ct);

        Assert.Equal("Bearer", tokens.TokenType);
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));
        var me = await OidcTestClient.Api(_api, tokens.AccessToken).GetFromJsonAsync<MyProfileResponse>("/api/v1/me", Ct);
        Assert.Equal(DevelopmentSeeder.AnaId, me!.UserId);
        Assert.Equal([Andina], me.Memberships.Select(m => m.TenantId));
    }

    [Fact]
    public async Task Credenciales_rechazadas_muestran_error_y_no_emiten_codigo()
    {
        var browser = new OidcTestClient(_api);
        var form = await browser.OpenLoginPageAsync(Ct);

        using var response = await browser.SubmitAsync(form, DevelopmentSeeder.BrunoEmail, "contraseña-incorrecta", "login", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("Correo o contraseña incorrectos.", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Un intento fallido no bloquea: luego entra con la contraseña correcta.
        var tokens = await new OidcTestClient(_api).LoginAsync(DevelopmentSeeder.BrunoEmail, db.DemoPassword, Ct);
        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
    }

    [Fact]
    public async Task Usuario_inexistente_recibe_el_mismo_mensaje_generico()
    {
        var browser = new OidcTestClient(_api);
        var form = await browser.OpenLoginPageAsync(Ct);

        using var response = await browser.SubmitAsync(form, "nadie@demo.local", db.DemoPassword, "login", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Correo o contraseña incorrectos.", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelar_en_la_pagina_devuelve_access_denied_al_escritorio()
    {
        var browser = new OidcTestClient(_api);
        var form = await browser.OpenLoginPageAsync(Ct);

        using var cancelled = await browser.SubmitAsync(form, string.Empty, string.Empty, "cancel", Ct);
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        using var callback = await browser.GetAsync(cancelled.Headers.Location!.OriginalString, Ct);

        var parameters = OidcTestClient.Callback(callback);
        Assert.Equal("access_denied", parameters["error"]);
        Assert.Equal(browser.State, parameters["state"]);
        Assert.False(parameters.ContainsKey("code"));
    }

    [Fact]
    public async Task Envio_sin_token_antifalsificacion_no_inicia_sesion()
    {
        var browser = new OidcTestClient(_api);
        var form = await browser.OpenLoginPageAsync(Ct);

        using var response = await browser.SubmitAsync(form with { AntiforgeryToken = "falso" }, DevelopmentSeeder.AnaEmail, db.DemoPassword, "login", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ReturnUrl_externo_se_rechaza_sin_redirigir()
    {
        using var response = await new OidcTestClient(_api).GetAsync("/account/login?ReturnUrl=https%3A%2F%2Fevil.example%2F", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Autorizacion_sin_PKCE_se_rechaza()
    {
        var browser = new OidcTestClient(_api);
        using var response = await browser.GetAsync(browser.AuthorizeUrl(withPkce: false), Ct);

        // El cliente exige PKCE: OpenIddict rechaza la solicitud sin pedir credenciales ni emitir código.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task PKCE_plain_se_rechaza_solo_se_admite_S256()
    {
        var browser = new OidcTestClient(_api);
        using var response = await browser.GetAsync(browser.AuthorizeUrl(challengeMethod: "plain"), Ct);

        Assert.DoesNotContain("/account/login", response.Headers.Location?.OriginalString ?? string.Empty, StringComparison.Ordinal);
        Assert.False(response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Redireccion_no_registrada_no_recibe_respuesta()
    {
        var browser = new OidcTestClient(_api);
        using var response = await browser.GetAsync(browser.AuthorizeUrl(redirectUri: "http://evil.example/callback"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Canje_con_code_verifier_incorrecto_se_rechaza()
    {
        var browser = new OidcTestClient(_api);
        var code = await browser.GetAuthorizationCodeAsync(DevelopmentSeeder.AnaEmail, db.DemoPassword, Ct);

        using var response = await browser.ExchangeCodeAsync(code, "verificador-que-no-corresponde-al-desafio-0000000", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_grant", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cada_inicio_pide_credenciales_de_nuevo()
    {
        var browser = new OidcTestClient(_api);
        await browser.LoginAsync(DevelopmentSeeder.AnaEmail, db.DemoPassword, Ct);

        // Mismo "navegador": la cookie de inicio se borró al emitir el código, así que vuelve a pedir la contraseña.
        using var again = await browser.GetAsync(browser.AuthorizeUrl(), Ct);
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Contains("/account/login", again.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_de_renovacion_entrega_un_nuevo_token_de_acceso()
    {
        var browser = new OidcTestClient(_api);
        var tokens = await browser.LoginAsync(DevelopmentSeeder.AnaEmail, db.DemoPassword, Ct);

        using var refreshed = await browser.RefreshAsync(tokens.RefreshToken!, Ct);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var renewed = await refreshed.Content.ReadFromJsonAsync<OidcTestClient.TokenResponse>(Ct);
        Assert.NotEqual(tokens.AccessToken, renewed!.AccessToken);
        using var me = await OidcTestClient.Api(_api, renewed.AccessToken).GetAsync("/api/v1/me", Ct);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Sin_token_responde_401_aunque_envie_empresa()
    {
        using var response = await OidcTestClient.Api(_api, accessToken: null, Andina).GetAsync("/api/v1/tenant/branches", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_invalido_o_alterado_responde_401()
    {
        var tokens = await new OidcTestClient(_api).LoginAsync(DevelopmentSeeder.AnaEmail, db.DemoPassword, Ct);
        var tampered = tokens.AccessToken[..^4] + (tokens.AccessToken.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        using var invalid = await OidcTestClient.Api(_api, "no-es-un-token").GetAsync("/api/v1/me", Ct);
        using var altered = await OidcTestClient.Api(_api, tampered).GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, altered.StatusCode);
    }

    [Fact]
    public async Task Token_expirado_responde_401()
    {
        await using var shortLived = _api.WithWebHostBuilder(b => b.UseSetting("Authentication:Oidc:AccessTokenLifetime", "00:00:02"));
        var tokens = await new OidcTestClient(shortLived).LoginAsync(DevelopmentSeeder.AnaEmail, db.DemoPassword, Ct);
        using (var fresh = await OidcTestClient.Api(shortLived, tokens.AccessToken).GetAsync("/api/v1/me", Ct))
        {
            Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        }

        await Task.Delay(TimeSpan.FromSeconds(4), Ct);

        using var expired = await OidcTestClient.Api(shortLived, tokens.AccessToken).GetAsync("/api/v1/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task Token_valido_con_empresa_ajena_responde_403()
    {
        var ana = await new OidcTestClient(_api).LoginAsync(DevelopmentSeeder.AnaEmail, db.DemoPassword, Ct);

        using var response = await OidcTestClient.Api(_api, ana.AccessToken, Pacifico).GetAsync("/api/v1/tenant/branches", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Token_valido_sin_permiso_de_escritura_responde_403()
    {
        // Carla es administradora en Andina pero solo "Consulta" en Pacífico.
        var carla = await new OidcTestClient(_api).LoginAsync(DevelopmentSeeder.CarlaEmail, db.DemoPassword, Ct);

        using var response = await OidcTestClient.Api(_api, carla.AccessToken, Pacifico)
            .PostAsJsonAsync("/api/v1/tenant/branches", new CreateBranchRequest("OIDC403", "No permitida", null, null), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Aislamiento_A_B_con_tokens_reales()
    {
        var ana = await new OidcTestClient(_api).LoginAsync(DevelopmentSeeder.AnaEmail, db.DemoPassword, Ct);
        var bruno = await new OidcTestClient(_api).LoginAsync(DevelopmentSeeder.BrunoEmail, db.DemoPassword, Ct);

        var andina = await OidcTestClient.Api(_api, ana.AccessToken, Andina)
            .GetFromJsonAsync<List<BranchResponse>>("/api/v1/tenant/branches", Ct);
        var pacifico = await OidcTestClient.Api(_api, bruno.AccessToken, Pacifico)
            .GetFromJsonAsync<List<BranchResponse>>("/api/v1/tenant/branches", Ct);

        Assert.Contains(andina!, b => b.Code == "MATRIZ");
        Assert.DoesNotContain(pacifico!, b => b.Code == "MATRIZ");
        Assert.DoesNotContain(pacifico!, b => andina!.Any(a => a.Id == b.Id));

        // Bruno intenta leer una sucursal de Andina por su Id desde su empresa: no la ve.
        using var guessed = await OidcTestClient.Api(_api, bruno.AccessToken, Pacifico)
            .GetAsync($"/api/v1/tenant/branches/{andina![0].Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);
    }

    [Fact]
    public async Task Cabecera_de_desarrollo_deshabilitada_no_autentica()
    {
        await using var noDevHeader = _api.WithWebHostBuilder(b => b.UseSetting("Authentication:DevelopmentHeader:Enabled", "false"));
        var client = noDevHeader.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Dev-User", DevelopmentSeeder.AnaEmail);

        using var response = await client.GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Cabecera_de_desarrollo_fuera_de_Development_impide_arrancar()
    {
        using var production = _api.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Authentication:DevelopmentHeader:Enabled", "true");
        });

        var ex = Assert.ThrowsAny<Exception>(() => production.CreateClient());
        Assert.Contains("fuera de Development/Testing", ex.ToString(), StringComparison.Ordinal);
    }
}
