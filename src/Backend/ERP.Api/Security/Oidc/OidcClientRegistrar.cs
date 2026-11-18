using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ERP.Api.Security.Oidc;

/// <summary>
/// Registra (o actualiza) al iniciar la API el cliente público de escritorio. Es configuración, no un secreto:
/// cliente nativo sin client_secret, solo Authorization Code con PKCE obligatorio, renovación y revocación.
/// </summary>
internal sealed class OidcClientRegistrar(IServiceScopeFactory scopes, OidcSettings settings, ILogger<OidcClientRegistrar> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var descriptor = CreateDescriptor(settings.DesktopClient);

            var existing = await manager.FindByClientIdAsync(descriptor.ClientId!, cancellationToken);
            if (existing is null)
            {
                await manager.CreateAsync(descriptor, cancellationToken);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Cliente OIDC {ClientId} registrado.", descriptor.ClientId);
                }
            }
            else
            {
                await manager.UpdateAsync(existing, descriptor, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin base de datos la API igual arranca (health/ready lo informa); el inicio de sesión fallará hasta reiniciar.
            logger.LogError(ex, "No se pudo registrar el cliente OIDC de escritorio.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static OpenIddictApplicationDescriptor CreateDescriptor(DesktopClientSettings client)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            DisplayName = client.DisplayName,
            ClientType = ClientTypes.Public,
            ApplicationType = ApplicationTypes.Native,
            ConsentType = ConsentTypes.Implicit,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Resource + OidcSettings.ApiResource,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        foreach (var uri in client.EffectiveRedirectUris)
        {
            descriptor.RedirectUris.Add(uri);
        }

        return descriptor;
    }
}
