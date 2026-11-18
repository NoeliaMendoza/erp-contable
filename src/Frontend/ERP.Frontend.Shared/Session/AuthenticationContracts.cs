namespace ERP.Frontend.Shared.Session;

// Contratos de sesión del escritorio (PLAT-01). Los consumen el cliente HTTP común y las pantallas de acceso;
// la implementación real (OIDC Authorization Code + PKCE con el navegador del sistema) vive detrás de estas
// interfaces para que nadie dependa de OpenIddict directamente. Ver docs/contratos-modulos.md › Sesión del escritorio.

/// <summary>Resultado de un intento de inicio de sesión interactivo.</summary>
public enum SignInStatus
{
    /// <summary>El usuario se autenticó y hay un token de acceso vigente.</summary>
    Succeeded = 1,

    /// <summary>El usuario canceló (botón Cancelar en el navegador o en la aplicación, o tiempo de espera agotado).</summary>
    Cancelled = 2,

    /// <summary>No se pudo completar (servidor caído, error de protocolo, cuenta inactiva...).</summary>
    Failed = 3,
}

/// <summary>Resultado de <see cref="IAuthenticationService.SignInAsync"/>. <c>Message</c> es apto para mostrar al usuario.</summary>
public sealed record SignInResult(SignInStatus Status, string? Message = null)
{
    public static SignInResult Success { get; } = new(SignInStatus.Succeeded);

    public bool Succeeded => Status == SignInStatus.Succeeded;
}

/// <summary>Motivo por el que terminó la sesión.</summary>
public enum SessionEndReason
{
    /// <summary>El usuario cerró sesión.</summary>
    SignedOut = 1,

    /// <summary>El token expiró y no se pudo renovar, o la API lo rechazó (401).</summary>
    Expired = 2,
}

public sealed class SessionEndedEventArgs(SessionEndReason reason) : EventArgs
{
    public SessionEndReason Reason { get; } = reason;
}

/// <summary>
/// Inicio y cierre de sesión del escritorio. Una sola instancia (singleton) por aplicación.
/// Los tokens se guardan solo en memoria: al cerrar la aplicación hay que volver a iniciar sesión.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>Hay una sesión con token de acceso (puede estar por renovarse).</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Abre el navegador del sistema en la página de inicio de sesión del ERP y espera el resultado.
    /// Cancelar el <paramref name="cancellationToken"/> devuelve <see cref="SignInStatus.Cancelled"/>.
    /// Nunca lanza por errores de red o de protocolo: los devuelve como <see cref="SignInStatus.Failed"/>.
    /// </summary>
    Task<SignInResult> SignInAsync(CancellationToken cancellationToken);

    /// <summary>Cierre de sesión local: descarta los tokens (y revoca el de renovación si es posible).</summary>
    Task SignOutAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Se dispara una vez cuando la sesión termina (cierre o expiración). Puede llegar desde un hilo
    /// que no es el de la interfaz: quien lo maneje debe pasar al hilo de UI.
    /// </summary>
    event EventHandler<SessionEndedEventArgs>? SessionEnded;
}

/// <summary>
/// Fuente del token de acceso para el cliente HTTP común (<c>SessionHeadersHandler</c>).
/// No exponga el token en registros, mensajes ni capturas.
/// </summary>
public interface IAccessTokenProvider
{
    /// <summary>
    /// Token de acceso vigente, renovándolo como máximo una vez si está por expirar.
    /// Devuelve <c>null</c> si no hay sesión (la API responderá 401).
    /// </summary>
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// La API respondió 401 a una solicitud enviada con <paramref name="rejectedAccessToken"/>. Si sigue siendo el token
    /// de la sesión actual, la termina con <see cref="SessionEndReason.Expired"/> (una respuesta tardía de una sesión anterior
    /// no cierra la nueva). El cliente HTTP NO reintenta la solicitud: así no hay bucles.
    /// </summary>
    void ReportUnauthorized(string rejectedAccessToken);
}
