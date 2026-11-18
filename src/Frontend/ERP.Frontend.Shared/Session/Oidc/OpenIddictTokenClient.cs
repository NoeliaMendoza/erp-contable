using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ERP.Frontend.Shared.Session.Oidc;

/// <summary>
/// <see cref="IOidcTokenClient"/> con el cliente oficial de OpenIddict: genera y valida <c>state</c>, <c>nonce</c> y PKCE
/// (S256), abre el navegador del sistema y recibe la respuesta en un servidor local de bucle (RFC 8252).
/// </summary>
public sealed class OpenIddictTokenClient(OpenIddictClientService service, ILogger<OpenIddictTokenClient> logger) : IOidcTokenClient
{
    private const string ServerUnavailable =
        "No se pudo contactar al servidor de inicio de sesión. Verifique que la API esté en ejecución y la dirección configurada.";

    private const string ClientMisconfigured =
        "No se pudo iniciar sesión por un problema de configuración de la aplicación. Contacte al administrador.";

    public async Task<OidcTokens> SignInInteractivelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var challenge = await service.ChallengeInteractivelyAsync(new()
            {
                CancellationToken = cancellationToken,
            });

            var result = await service.AuthenticateInteractivelyAsync(new()
            {
                CancellationToken = cancellationToken,
                Nonce = challenge.Nonce,
            });

            return ToTokens(result.BackchannelAccessToken, result.BackchannelAccessTokenExpirationDate, result.RefreshToken);
        }
        catch (OpenIddictExceptions.ProtocolException ex) when (ex.Error is Errors.AccessDenied)
        {
            throw new OidcSignInException("Inicio de sesión cancelado.", cancelled: true, ex);
        }
        catch (OpenIddictExceptions.ProtocolException ex)
        {
            throw new OidcSignInException(
                ex.Error is Errors.InvalidGrant or Errors.LoginRequired
                    ? "No se pudo iniciar sesión con esa cuenta. Intente de nuevo."
                    : "El servidor rechazó el inicio de sesión. Intente de nuevo o contacte al administrador.",
                inner: ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Servidor de inicio de sesión inaccesible.");
            throw new OidcSignInException(ServerUnavailable, inner: ex);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is HttpRequestException)
        {
            logger.LogWarning(ex, "Servidor de inicio de sesión inaccesible.");
            throw new OidcSignInException(ServerUnavailable, inner: ex);
        }
        catch (InvalidOperationException ex)
        {
            // Configuración del cliente o del emisor (discovery inválido, emisor distinto, puerto local ocupado...).
            logger.LogError(ex, "Fallo de configuración del inicio de sesión OIDC.");
            throw new OidcSignInException(ClientMisconfigured, inner: ex);
        }
    }

    public async Task<OidcTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.AuthenticateWithRefreshTokenAsync(new()
            {
                CancellationToken = cancellationToken,
                RefreshToken = refreshToken,
            });

            return ToTokens(result.AccessToken, result.AccessTokenExpirationDate, result.RefreshToken ?? refreshToken);
        }
        catch (OpenIddictExceptions.ProtocolException ex)
        {
            throw new OidcSignInException("La sesión expiró.", inner: ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new OidcSignInException(ServerUnavailable, inner: ex);
        }
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            await service.RevokeTokenAsync(new()
            {
                CancellationToken = cancellationToken,
                Token = refreshToken,
                TokenTypeHint = TokenTypeHints.RefreshToken,
            });
        }
        catch (Exception ex) when (ex is OpenIddictExceptions.ProtocolException or InvalidOperationException)
        {
            throw new OidcSignInException("No se pudo revocar el token.", inner: ex);
        }
    }

    private static OidcTokens ToTokens(string? accessToken, DateTimeOffset? expiresAt, string? refreshToken) =>
        string.IsNullOrEmpty(accessToken)
            ? throw new OidcSignInException("El servidor no entregó un token de acceso.")
            : new OidcTokens(accessToken, expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5), refreshToken);
}

/// <summary>Registro de la sesión OIDC del escritorio en el contenedor de dependencias.</summary>
public static class DesktopAuthenticationRegistration
{
    public const string ClientId = "erp-desktop";

    /// <summary>
    /// Cliente público (sin client_secret) contra el emisor <paramref name="issuer"/> (la propia API). La respuesta llega a
    /// <c>http://localhost:{redirectPort}/callback/login/local</c>, que debe estar registrado en la API.
    /// Requiere un <c>IHost</c> en ejecución (servidor local de bucle del navegador).
    /// </summary>
    public static IServiceCollection AddDesktopAuthentication(this IServiceCollection services, Uri issuer, int redirectPort)
    {
        services.AddOpenIddict().AddClient(options =>
        {
            options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();

            // El escritorio no tiene base de datos: los tokens viven en memoria (OidcAuthenticationService) y el "state"
            // del flujo va cifrado y firmado con claves efímeras de este proceso, con vida corta.
            options.DisableTokenStorage();

            // Solo protegen el estado del flujo dentro de este proceso; no son credenciales del cliente.
            options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();

            options.UseSystemIntegration()
                .DisableActivationHandling()
                .DisableActivationRedirection()
                .DisablePipeServer()
                .SetAllowedEmbeddedWebServerPorts(redirectPort)
                .SetAuthenticationTimeout(TimeSpan.FromMinutes(5));

            options.UseSystemNetHttp().SetProductInformation(typeof(DesktopAuthenticationRegistration).Assembly);

            options.AddRegistration(new OpenIddictClientRegistration
            {
                Issuer = issuer,
                ClientId = ClientId,
                RedirectUri = new Uri("callback/login/local", UriKind.Relative),
                Scopes = { Scopes.Email, Scopes.Profile, Scopes.OfflineAccess },
            });
        });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOidcTokenClient, OpenIddictTokenClient>();
        services.AddSingleton<OidcAuthenticationService>();
        services.AddSingleton<IAuthenticationService>(sp => sp.GetRequiredService<OidcAuthenticationService>());
        services.AddSingleton<IAccessTokenProvider>(sp => sp.GetRequiredService<OidcAuthenticationService>());
        return services;
    }
}
