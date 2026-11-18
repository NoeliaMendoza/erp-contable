using System.Security.Claims;
using ERP.BuildingBlocks.Tenancy;
using ERP.Modules.Platform.Models;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using OidcErrors = OpenIddict.Abstractions.OpenIddictConstants.Errors;

namespace ERP.Api.Security.Oidc;

/// <summary>
/// Endpoints del emisor OIDC y la página de inicio de sesión. Son anónimos por naturaleza (los usa el navegador antes de
/// existir un token), por eso viven en el host y no en <c>Controllers/</c> de un módulo.
/// <list type="bullet">
/// <item><c>/connect/authorize</c>: exige la cookie de inicio de sesión; si falta, redirige a <c>/account/login</c>.</item>
/// <item><c>/connect/token</c>: canjea el código (con PKCE) o el token de renovación, comprobando que la persona siga activa.</item>
/// <item><c>/account/login</c>: formulario de correo y contraseña (Identity), con antifalsificación y bloqueo por intentos.</item>
/// </list>
/// </summary>
internal static class OidcEndpoints
{
    public const string AuthorizePath = "/connect/authorize";
    public const string TokenPath = "/connect/token";
    public const string LoginPath = "/account/login";

    /// <summary>Marca que agrega la página de inicio de sesión cuando el usuario pulsa Cancelar.</summary>
    internal const string CancelParameter = "erp_cancel";

    public static IEndpointRouteBuilder MapErpOidcEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods(AuthorizePath, [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync).ExcludeFromDescription();
        endpoints.MapPost(TokenPath, ExchangeAsync).ExcludeFromDescription();
        endpoints.MapGet(LoginPath, ShowLogin).ExcludeFromDescription();
        endpoints.MapPost(LoginPath, SubmitLoginAsync).ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(HttpContext context, IUserDirectory directory)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("No se pudo leer la solicitud OpenID Connect.");

        if (request.GetParameter(CancelParameter) is not null)
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Deny(OidcErrors.AccessDenied, "El usuario canceló el inicio de sesión.");
        }

        var login = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!login.Succeeded || !Guid.TryParse(login.Principal.FindFirstValue(Claims.Subject), out var userId))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Deny(OidcErrors.LoginRequired, "Se requiere iniciar sesión.");
            }

            var returnUrl = context.Request.PathBase + context.Request.Path + QueryString.Create(
                request.GetParameters().Select(p => KeyValuePair.Create(p.Key, (StringValues)p.Value.ToString())));
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUrl },
                [IdentityConstants.ApplicationScheme]);
        }

        // La cookie de inicio de sesión dura solo este intercambio: cada inicio desde el escritorio pide credenciales,
        // así "Cerrar sesión" en la aplicación no deja una sesión reutilizable en el navegador.
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);

        var user = await directory.FindActiveUserAsync(userId, context.RequestAborted);
        if (user is null)
        {
            return Deny(OidcErrors.AccessDenied, "La cuenta no está activa.");
        }

        var principal = CreatePrincipal(user, request.GetScopes());
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> ExchangeAsync(HttpContext context, IUserDirectory directory, UserManager<ErpIdentityUser> users)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("No se pudo leer la solicitud OpenID Connect.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Deny(OidcErrors.UnsupportedGrantType, "Tipo de concesión no admitido.");
        }

        // OpenIddict ya validó el código/token, el PKCE (code_verifier), el cliente y la redirección.
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!Guid.TryParse(result.Principal?.GetClaim(Claims.Subject), out var userId))
        {
            return Deny(OidcErrors.InvalidGrant, "El token ya no es válido.");
        }

        var user = await directory.FindActiveUserAsync(userId, context.RequestAborted);
        var credentials = await users.FindByIdAsync(userId.ToString());
        if (user is null || credentials is null || await users.IsLockedOutAsync(credentials))
        {
            return Deny(OidcErrors.InvalidGrant, "La cuenta ya no puede iniciar sesión.");
        }

        return Results.SignIn(CreatePrincipal(user, result.Principal!.GetScopes()),
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult ShowLogin(HttpContext context, IAntiforgery antiforgery, string? returnUrl) =>
        IsValidReturnUrl(returnUrl)
            ? LoginPage.Render(context, antiforgery, returnUrl!, error: null)
            : LoginPage.RenderInvalidRequest(context);

    private static async Task<IResult> SubmitLoginAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        UserManager<ErpIdentityUser> users,
        SignInManager<ErpIdentityUser> signIn,
        IUserDirectory directory,
        ILoggerFactory loggers)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var returnUrl = form["returnUrl"].ToString();
        if (!IsValidReturnUrl(returnUrl))
        {
            return LoginPage.RenderInvalidRequest(context);
        }

        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return LoginPage.Render(context, antiforgery, returnUrl, "La página expiró. Vuelva a intentarlo.");
        }

        if (form["action"] == "cancel")
        {
            return Results.Redirect($"{returnUrl}&{CancelParameter}=true");
        }

        const string rejected = "Correo o contraseña incorrectos.";
        var email = form["email"].ToString().Trim();
        var password = form["password"].ToString();
        if (email.Length == 0 || password.Length == 0)
        {
            return LoginPage.Render(context, antiforgery, returnUrl, "Ingrese su correo y su contraseña.", email);
        }

        var credentials = await users.FindByEmailAsync(email);
        if (credentials is null || await directory.FindActiveUserAsync(credentials.Id, context.RequestAborted) is null)
        {
            return LoginPage.Render(context, antiforgery, returnUrl, rejected, email);
        }

        var check = await signIn.CheckPasswordSignInAsync(credentials, password, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            loggers.CreateLogger(typeof(OidcEndpoints)).LogWarning("Cuenta {UserId} bloqueada por intentos fallidos.", credentials.Id);
            return LoginPage.Render(context, antiforgery, returnUrl,
                "La cuenta está bloqueada temporalmente por varios intentos fallidos. Intente de nuevo en unos minutos.", email);
        }

        if (!check.Succeeded)
        {
            return LoginPage.Render(context, antiforgery, returnUrl, rejected, email);
        }

        var identity = new ClaimsIdentity([new Claim(Claims.Subject, credentials.Id.ToString())], IdentityConstants.ApplicationScheme);
        await context.SignInAsync(IdentityConstants.ApplicationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect(returnUrl);
    }

    /// <summary>Solo se vuelve a la autorización OIDC de este mismo servidor (evita redirecciones abiertas).</summary>
    internal static bool IsValidReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith(AuthorizePath + "?", StringComparison.Ordinal)
        && !returnUrl.Contains('\\', StringComparison.Ordinal)
        && Uri.TryCreate(returnUrl, UriKind.Relative, out _);

    private static ClaimsPrincipal CreatePrincipal(DirectoryUser user, IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.DisplayName);

        // Sin roles en el token: los permisos son por empresa y la API los consulta en cada solicitud.
        identity.SetScopes(scopes);
        identity.SetResources(OidcSettings.ApiResource);
        identity.SetDestinations(static claim => claim.Type switch
        {
            Claims.Name or Claims.Email when claim.Subject?.HasScope(claim.Type == Claims.Name ? Scopes.Profile : Scopes.Email) == true =>
                [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken],
        });

        return new ClaimsPrincipal(identity);
    }

    private static IResult Deny(string error, string description) => Results.Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }),
        [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
