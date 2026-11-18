using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;

namespace ERP.IntegrationTests.Infrastructure;

/// <summary>
/// Hace de "navegador + escritorio" contra la API real: Authorization Code + PKCE (S256) con la página de inicio de sesión,
/// cookies y antifalsificación reales. Así se prueba el protocolo completo sin abrir un navegador.
/// </summary>
internal sealed partial class OidcTestClient(WebApplicationFactory<Program> factory)
{
    public const string ClientId = "erp-desktop";
    public const string RedirectUri = "http://localhost:7890/callback/login/local";

    private readonly HttpClient _browser = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
    });

    public string CodeVerifier { get; } = Base64Url(RandomNumberGenerator.GetBytes(32));

    public string State { get; } = Base64Url(RandomNumberGenerator.GetBytes(16));

    public string AuthorizeUrl(string? redirectUri = null, bool withPkce = true, string challengeMethod = "S256")
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri ?? RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid offline_access email profile",
            ["state"] = State,
            ["nonce"] = Base64Url(RandomNumberGenerator.GetBytes(16)),
        };
        if (withPkce)
        {
            query["code_challenge"] = challengeMethod == "S256"
                ? Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(CodeVerifier)))
                : CodeVerifier;
            query["code_challenge_method"] = challengeMethod;
        }

        return QueryHelpers.AddQueryString("/connect/authorize", query);
    }

    public Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct) => _browser.GetAsync(url, ct);

    /// <summary>Abre la autorización y devuelve la página de inicio de sesión (y sus campos ocultos).</summary>
    public async Task<LoginForm> OpenLoginPageAsync(CancellationToken ct)
    {
        using var authorize = await _browser.GetAsync(AuthorizeUrl(), ct);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var loginUrl = authorize.Headers.Location!.OriginalString;
        Assert.Contains("/account/login?ReturnUrl=", loginUrl, StringComparison.Ordinal);

        using var page = await _browser.GetAsync(loginUrl, ct);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return LoginForm.Parse(await page.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Envía el formulario de inicio de sesión (o Cancelar) tal como lo haría el navegador.</summary>
    public Task<HttpResponseMessage> SubmitAsync(LoginForm form, string email, string password, string action, CancellationToken ct) =>
        _browser.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [form.AntiforgeryField] = form.AntiforgeryToken,
            ["returnUrl"] = form.ReturnUrl,
            ["email"] = email,
            ["password"] = password,
            ["action"] = action,
        }), ct);

    /// <summary>Inicio completo: credenciales → código en la redirección local → canje con code_verifier.</summary>
    public async Task<TokenResponse> LoginAsync(string email, string password, CancellationToken ct)
    {
        var code = await GetAuthorizationCodeAsync(email, password, ct);
        using var response = await ExchangeCodeAsync(code, CodeVerifier, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(ct))!;
    }

    public async Task<string> GetAuthorizationCodeAsync(string email, string password, CancellationToken ct)
    {
        var form = await OpenLoginPageAsync(ct);
        using var submitted = await SubmitAsync(form, email, password, "login", ct);
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);

        using var authorized = await _browser.GetAsync(submitted.Headers.Location!.OriginalString, ct);
        Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode);
        var callback = Callback(authorized);
        Assert.Equal(State, callback["state"]);
        return callback["code"].ToString();
    }

    public Task<HttpResponseMessage> ExchangeCodeAsync(string code, string verifier, CancellationToken ct) =>
        _browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier,
        }), ct);

    public Task<HttpResponseMessage> RefreshAsync(string refreshToken, CancellationToken ct) =>
        _browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
        }), ct);

    /// <summary>Parámetros de la redirección al escritorio (http://localhost:7890/callback/...).</summary>
    public static Dictionary<string, Microsoft.Extensions.Primitives.StringValues> Callback(HttpResponseMessage response)
    {
        var location = response.Headers.Location!;
        Assert.StartsWith(RedirectUri, location.OriginalString, StringComparison.Ordinal);
        return QueryHelpers.ParseQuery(location.Query);
    }

    /// <summary>Cliente "de escritorio" con Bearer y, opcionalmente, empresa seleccionada.</summary>
    public static HttpClient Api(WebApplicationFactory<Program> factory, string? accessToken, Guid? tenantId = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (tenantId is not null)
        {
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.Value.ToString());
        }

        return client;
    }

    private static string Base64Url(byte[] bytes) => WebEncoders.Base64UrlEncode(bytes);

    internal sealed partial record LoginForm(string AntiforgeryField, string AntiforgeryToken, string ReturnUrl, string Html)
    {
        public static LoginForm Parse(string html)
        {
            var antiforgery = AntiforgeryInput().Match(html);
            var returnUrl = ReturnUrlInput().Match(html);
            Assert.True(antiforgery.Success, "La página no incluye el token antifalsificación.");
            Assert.True(returnUrl.Success, "La página no incluye returnUrl.");
            return new LoginForm(
                antiforgery.Groups["name"].Value,
                WebUtility.HtmlDecode(antiforgery.Groups["value"].Value),
                WebUtility.HtmlDecode(returnUrl.Groups["value"].Value),
                html);
        }

        [GeneratedRegex("""name="(?<name>__RequestVerificationToken)" value="(?<value>[^"]*)""")]
        private static partial Regex AntiforgeryInput();

        [GeneratedRegex("""name="returnUrl" value="(?<value>[^"]*)""")]
        private static partial Regex ReturnUrlInput();
    }

    internal sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string TokenType);
}
