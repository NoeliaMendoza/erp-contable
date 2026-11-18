namespace ERP.Api.Security.Oidc;

/// <summary>
/// Configuración del emisor OIDC (sección <c>Authentication:Oidc</c>). Los valores por defecto sirven para desarrollo;
/// en producción se fijan por variables de entorno y los certificados NUNCA se versionan.
/// </summary>
internal sealed class OidcSettings
{
    public const string Section = "Authentication:Oidc";

    /// <summary>Audiencia/recurso de los tokens de acceso que acepta la API (URI absoluto, RFC 8707).</summary>
    public const string ApiResource = "urn:orvix:erp-api";

    /// <summary>Vigencia del token de acceso. Corta: un usuario desactivado pierde acceso al expirar.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Vigencia del token de renovación (jornada laboral).</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromHours(8);

    public DesktopClientSettings DesktopClient { get; set; } = new();

    /// <summary>Certificado de firma (.pfx) fuera de Development/Testing.</summary>
    public CertificateSettings? SigningCertificate { get; set; }

    /// <summary>Certificado de cifrado (.pfx) fuera de Development/Testing.</summary>
    public CertificateSettings? EncryptionCertificate { get; set; }
}

/// <summary>Cliente público de escritorio (Avalonia): sin client_secret, Authorization Code + PKCE.</summary>
internal sealed class DesktopClientSettings
{
    public string ClientId { get; set; } = "erp-desktop";

    public string DisplayName { get; set; } = "Orvix escritorio";

    public const string DefaultRedirectUri = "http://localhost:7890/callback/login/local";

    /// <summary>
    /// Redirecciones de bucle local (RFC 8252) donde el escritorio escucha la respuesta. Deben coincidir con
    /// <c>ERP_OIDC_REDIRECT_PORT</c> del escritorio. Vacío = <see cref="DefaultRedirectUri"/> (puerto 7890).
    /// </summary>
    public List<string> RedirectUris { get; set; } = [];

    public IReadOnlyList<Uri> EffectiveRedirectUris =>
        [.. (RedirectUris.Count == 0 ? [DefaultRedirectUri] : RedirectUris).Select(u => new Uri(u, UriKind.Absolute))];
}

internal sealed class CertificateSettings
{
    public string Path { get; set; } = string.Empty;

    /// <summary>Contraseña del .pfx: solo por User Secrets o variables de entorno.</summary>
    public string? Password { get; set; }
}
