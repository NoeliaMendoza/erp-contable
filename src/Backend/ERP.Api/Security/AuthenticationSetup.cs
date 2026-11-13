using System.Security.Claims;
using System.Text.Encodings.Web;
using ERP.BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ERP.Api.Security;

internal static class AuthenticationSetup
{
    public const string DevelopmentScheme = "DevelopmentHeader";
    public const string DevelopmentUserHeader = "X-Dev-User";

    /// <summary>
    /// Autenticación. Estado actual:
    /// <list type="bullet">
    /// <item><b>Implementado:</b> esquema de desarrollo por cabecera <c>X-Dev-User</c> (email de un usuario semilla),
    /// solo en entornos Development/Testing y con <c>Authentication:DevelopmentHeader:Enabled=true</c>.</item>
    /// <item><b>Pendiente (Issue PLAT-01):</b> ASP.NET Core Identity + OpenIddict con Authorization Code + PKCE
    /// y navegador del sistema para Avalonia. Hasta entonces la API se niega a arrancar en otros entornos.</item>
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

        if (!devEnabled)
        {
            throw new InvalidOperationException(
                "No hay un esquema de autenticación de producción configurado (OpenIddict pendiente, Issue PLAT-01).");
        }

        services.AddAuthentication(DevelopmentScheme)
            .AddScheme<AuthenticationSchemeOptions, DevelopmentHeaderAuthenticationHandler>(DevelopmentScheme, _ => { });

        return services;
    }
}

/// <summary>
/// Solo desarrollo/pruebas: autentica al usuario semilla indicado en <c>X-Dev-User</c>.
/// No valida contraseña. Nunca se registra en producción (ver <see cref="AuthenticationSetup"/>).
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
