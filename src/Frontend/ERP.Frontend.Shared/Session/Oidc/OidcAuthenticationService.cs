namespace ERP.Frontend.Shared.Session.Oidc;

/// <summary>Tokens de una sesión. Solo viven en memoria de este proceso.</summary>
public sealed record OidcTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string? RefreshToken);

/// <summary>Error de inicio de sesión con un mensaje apto para el usuario (sin detalles técnicos ni tokens).</summary>
public sealed class OidcSignInException(string userMessage, bool cancelled = false, Exception? inner = null)
    : Exception(userMessage, inner)
{
    /// <summary>El servidor devolvió <c>access_denied</c> (el usuario pulsó Cancelar en la página de inicio de sesión).</summary>
    public bool Cancelled { get; } = cancelled;
}

/// <summary>
/// Operaciones de protocolo OIDC. La implementación real es <see cref="OpenIddictTokenClient"/>; existe como interfaz
/// para probar la lógica de sesión sin navegador.
/// </summary>
public interface IOidcTokenClient
{
    /// <summary>Authorization Code + PKCE con el navegador del sistema. Lanza <see cref="OperationCanceledException"/> o <see cref="OidcSignInException"/>.</summary>
    Task<OidcTokens> SignInInteractivelyAsync(CancellationToken cancellationToken);

    Task<OidcTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}

/// <summary>
/// Sesión del escritorio: implementa <see cref="IAuthenticationService"/> y <see cref="IAccessTokenProvider"/>.
/// <list type="bullet">
/// <item>Tokens solo en memoria (no se persisten ni se registran).</item>
/// <item>Renovación: si el token vence en menos de <see cref="RefreshMargin"/>, se renueva <b>una vez</b> (una sola
/// renovación simultánea); si falla, la sesión termina con <see cref="SessionEndReason.Expired"/>.</item>
/// <item><see cref="SessionEnded"/> se emite una sola vez por sesión: ni reintentos ni bucles.</item>
/// </list>
/// </summary>
public sealed class OidcAuthenticationService(IOidcTokenClient client, TimeProvider clock) : IAuthenticationService, IAccessTokenProvider, IDisposable
{
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private OidcTokens? _tokens;

    public event EventHandler<SessionEndedEventArgs>? SessionEnded;

    public bool IsAuthenticated => Volatile.Read(ref _tokens) is not null;

    public async Task<SignInResult> SignInAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tokens = await client.SignInInteractivelyAsync(cancellationToken);
            Volatile.Write(ref _tokens, tokens);
            return SignInResult.Success;
        }
        catch (OperationCanceledException)
        {
            return new SignInResult(SignInStatus.Cancelled, "Inicio de sesión cancelado.");
        }
        catch (OidcSignInException ex)
        {
            return new SignInResult(ex.Cancelled ? SignInStatus.Cancelled : SignInStatus.Failed, ex.Message);
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var previous = Interlocked.Exchange(ref _tokens, null);
        if (previous is null)
        {
            return;
        }

        SessionEnded?.Invoke(this, new SessionEndedEventArgs(SessionEndReason.SignedOut));

        if (previous.RefreshToken is { } refreshToken)
        {
            // Mejor esfuerzo: si el servidor no responde, el token de renovación igual se descartó localmente.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await client.RevokeAsync(refreshToken, timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or OidcSignInException or HttpRequestException)
            {
            }
        }
    }

    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _tokens);
        if (current is null)
        {
            return null;
        }

        if (current.AccessTokenExpiresAt - clock.GetUtcNow() > RefreshMargin)
        {
            return current.AccessToken;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            // Otra solicitud pudo renovar (o cerrar) la sesión mientras se esperaba.
            var latest = Volatile.Read(ref _tokens);
            if (latest is null || !ReferenceEquals(latest, current))
            {
                return latest?.AccessToken;
            }

            if (latest.RefreshToken is null)
            {
                if (latest.AccessTokenExpiresAt > clock.GetUtcNow())
                {
                    return latest.AccessToken;
                }

                End(latest);
                return null;
            }

            try
            {
                var renewed = await client.RefreshAsync(latest.RefreshToken, cancellationToken);
                Interlocked.CompareExchange(ref _tokens, renewed, latest);
                return Volatile.Read(ref _tokens)?.AccessToken;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is OidcSignInException or HttpRequestException or OperationCanceledException)
            {
                End(latest);
                return null;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void ReportUnauthorized(string rejectedAccessToken)
    {
        var current = Volatile.Read(ref _tokens);
        if (current is not null && string.Equals(current.AccessToken, rejectedAccessToken, StringComparison.Ordinal))
        {
            End(current);
        }
    }

    public void Dispose() => _refreshGate.Dispose();

    private void End(OidcTokens expected)
    {
        if (ReferenceEquals(Interlocked.CompareExchange(ref _tokens, null, expected), expected))
        {
            SessionEnded?.Invoke(this, new SessionEndedEventArgs(SessionEndReason.Expired));
        }
    }
}
