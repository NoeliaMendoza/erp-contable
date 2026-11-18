using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using ERP.Api.Security.Oidc;
using ERP.BuildingBlocks.Tenancy;
using ERP.Modules.Platform.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ERP.Api.Security;

internal static class AuthenticationSetup
{
    /// <summary>Esquema por defecto: decide por solicitud entre el token Bearer y la cabecera de desarrollo.</summary>
    public const string DefaultScheme = "Erp";

    public const string DevelopmentScheme = "DevelopmentHeader";
    public const string DevelopmentUserHeader = "X-Dev-User";

    /// <summary>
    /// Autenticación real (PLAT-01):
    /// <list type="bullet">
    /// <item><b>Emisor OIDC</b> (OpenIddict) en esta misma API: Authorization Code + PKCE obligatorio, cliente público de
    /// escritorio sin client_secret, tokens de acceso cortos y renovación (<c>offline_access</c>).</item>
    /// <item><b>Inicio de sesión</b> con ASP.NET Core Identity en una página del servidor que abre el navegador del sistema
    /// (cookie <c>Identity.Application</c> solo para esa página: nunca autentica la API).</item>
    /// <item><b>API</b>: <c>Authorization: Bearer</c> validado localmente (firma, cifrado, emisor, audiencia y expiración).</item>
    /// <item><b>X-Dev-User</b>: solo Development/Testing con <c>Authentication:DevelopmentHeader:Enabled=true</c>
    /// (pruebas y curl). Fuera de esos entornos la API se niega a arrancar si alguien lo habilita.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddErpAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var devEnabled = configuration.GetValue<bool>("Authentication:DevelopmentHeader:Enabled");
        var devEnvironment = environment.IsDevelopment() || environment.IsEnvironment("Testing");

        if (devEnabled && !devEnvironment)
        {
            throw new InvalidOperationException("La autenticación de desarrollo no puede habilitarse fuera de Development/Testing.");
        }

        var settings = configuration.GetSection(OidcSettings.Section).Get<OidcSettings>() ?? new OidcSettings();
        services.AddSingleton(settings);

        var authentication = services.AddAuthentication(options =>
            {
                options.DefaultScheme = DefaultScheme;
                options.DefaultChallengeScheme = DefaultScheme;
                options.DefaultForbidScheme = DefaultScheme;
            })
            .AddPolicyScheme(DefaultScheme, "Bearer o desarrollo", options => options.ForwardDefaultSelector = context =>
                devEnabled
                && string.IsNullOrEmpty(context.Request.Headers.Authorization)
                && context.Request.Headers.ContainsKey(DevelopmentUserHeader)
                    ? DevelopmentScheme
                    : OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                // Solo para la página de inicio de sesión del navegador: se borra al emitir el código.
                options.Cookie.Name = "erp.login";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                options.SlidingExpiration = false;
                options.LoginPath = OidcEndpoints.LoginPath;
            });

        if (devEnabled)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, DevelopmentHeaderAuthenticationHandler>(DevelopmentScheme, _ => { });
        }

        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "erp.antiforgery";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.FormFieldName = "__RequestVerificationToken";
        });

        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AuthDbContext>().ReplaceDefaultEntities<Guid>())
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris(OidcEndpoints.AuthorizePath.TrimStart('/'))
                    .SetTokenEndpointUris(OidcEndpoints.TokenPath.TrimStart('/'))
                    .SetRevocationEndpointUris("connect/revoke");

                options.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow();

                // Solo PKCE S256 (OAuth 2.1): se rechaza el método "plain".
                options.Configure(o =>
                {
                    o.CodeChallengeMethods.Clear();
                    o.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                });

                options.RegisterScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess);
                options.RegisterResources(OidcSettings.ApiResource);

                options.SetAccessTokenLifetime(settings.AccessTokenLifetime)
                    .SetRefreshTokenLifetime(settings.RefreshTokenLifetime)
                    .SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(2));

                AddCredentials(options, settings, environment);

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.AddAudiences(OidcSettings.ApiResource);
                options.UseAspNetCore();
            });

        services.AddHostedService<OidcClientRegistrar>();

        return services;
    }

    /// <summary>
    /// Claves del emisor. Testing: efímeras (en memoria). Development: certificados de desarrollo del perfil de Windows.
    /// Otros entornos: certificados .pfx configurados fuera del repositorio; si faltan, la API no arranca.
    /// </summary>
    private static void AddCredentials(OpenIddictServerBuilder options, OidcSettings settings, IHostEnvironment environment)
    {
        if (environment.IsEnvironment("Testing"))
        {
            options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
            return;
        }

        if (environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
            return;
        }

        options.AddSigningCertificate(LoadCertificate(settings.SigningCertificate, "SigningCertificate"))
            .AddEncryptionCertificate(LoadCertificate(settings.EncryptionCertificate, "EncryptionCertificate"));
    }

    private static X509Certificate2 LoadCertificate(CertificateSettings? certificate, string name)
    {
        if (certificate is null || string.IsNullOrWhiteSpace(certificate.Path))
        {
            throw new InvalidOperationException(
                $"Falta {OidcSettings.Section}:{name}:Path. Fuera de Development/Testing el emisor OIDC necesita certificados (.pfx) fuera del repositorio.");
        }

        return X509CertificateLoader.LoadPkcs12FromFile(certificate.Path, certificate.Password);
    }
}

/// <summary>
/// Solo desarrollo/pruebas: autentica al usuario semilla indicado en <c>X-Dev-User</c>.
/// No valida contraseña. Nunca se registra fuera de Development/Testing (ver <see cref="AuthenticationSetup"/>).
/// </summary>
internal sealed class DevelopmentHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IUserDirectory users)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var email = Request.Headers[AuthenticationSetup.DevelopmentUserHeader].ToString();
        if (string.IsNullOrWhiteSpace(email))
        {
            return AuthenticateResult.NoResult();
        }

        var userId = await users.FindActiveUserIdByEmailAsync(email, Context.RequestAborted);
        if (userId is null)
        {
            return AuthenticateResult.Fail("Usuario de desarrollo desconocido o inactivo.");
        }

        var identity = new ClaimsIdentity(
            [new Claim("sub", userId.Value.ToString()), new Claim(ClaimTypes.Email, email)],
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
