namespace ERP.Tributario.Sri;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Contratos del submódulo SRI (comprobantes electrónicos).
// ESTADO: solo interfaces. No hay adaptador real, ni endpoints, ni códigos de catálogo fijados.
// Los códigos oficiales (tipo de comprobante, tipo de identificación, impuestos, etc.) deben tomarse
// de la ficha técnica vigente del SRI y registrarse en docs/ecuador/matriz-normativa.md.
// Por defecto se usan simuladores o el ambiente oficial de pruebas; nunca producción sin autorización
// explícita del dueño del certificado y de las credenciales.
// ─────────────────────────────────────────────────────────────────────────────────────────────

public enum ElectronicDocumentType
{
    Factura = 1,
    NotaCredito = 2,
    NotaDebito = 3,
    ComprobanteRetencion = 4,
    GuiaRemision = 5,
}

public enum SriEnvironment
{
    Pruebas = 1,
    Produccion = 2,
}

/// <summary>
/// Estados internos del ciclo de emisión. Se reflejan respuestas reales del SRI; nunca se marca
/// <see cref="Autorizado"/> sin respuesta de autorización verificable.
/// La secuencia exacta exigida por el SRI está pendiente de confirmación (ver docs/ecuador/facturacion-electronica.md).
/// </summary>
public enum ElectronicDocumentStatus
{
    Borrador = 0,
    ListoParaEnvio = 1,
    Enviado = 2,
    Recibido = 3,
    Devuelto = 4,
    Pendiente = 5,
    Autorizado = 6,
    Rechazado = 7,
}

/// <summary>Identifica al emisor para generar comprobantes (sin datos secretos).</summary>
public sealed record EmitterReference(Guid TenantId, Guid TaxpayerId, string EstablishmentCode, string EmissionPointCode);

/// <summary>Referencia opaca a un certificado de firma (p. ej. PKCS#12) guardado fuera del repositorio.</summary>
public sealed record CertificateReference(Guid TenantId, Guid TaxpayerId, string SecretName);

public sealed record SriMessage(string Identifier, string Message, string? AdditionalInfo, string Type);

public sealed record SriReceptionResult(string AccessKey, ElectronicDocumentStatus Status, IReadOnlyList<SriMessage> Messages, string CorrelationId);

public sealed record SriAuthorizationResult(
    string AccessKey,
    ElectronicDocumentStatus Status,
    string? AuthorizationNumber,
    DateTimeOffset? AuthorizedAtUtc,
    IReadOnlyList<SriMessage> Messages,
    string CorrelationId);

/// <summary>Genera la clave de acceso según la ficha técnica vigente (algoritmo pendiente de verificación).</summary>
public interface IAccessKeyGenerator
{
    string Generate(EmitterReference emitter, ElectronicDocumentType type, DateOnly issueDate, long sequential, SriEnvironment environment);
}

/// <summary>Construye el XML del comprobante contra el XSD vigente de la versión indicada.</summary>
public interface IElectronicDocumentXmlBuilder
{
    ElectronicDocumentType DocumentType { get; }

    string SchemaVersion { get; }

    Task<string> BuildAsync(object document, CancellationToken cancellationToken);
}

/// <summary>Firma el XML. La contraseña del certificado se obtiene de un almacén de secretos; nunca del repositorio ni de Avalonia.</summary>
public interface IElectronicDocumentSigner
{
    Task<string> SignAsync(string xml, CertificateReference certificate, CancellationToken cancellationToken);
}

/// <summary>
/// Adaptador de comunicación con el SRI. Implementaciones esperadas: simulador local, ambiente de pruebas
/// y producción (esta última solo con autorización explícita). Debe ser idempotente por clave de acceso.
/// </summary>
public interface ISriGateway
{
    SriEnvironment Environment { get; }

    Task<SriReceptionResult> SendAsync(string signedXml, string accessKey, CancellationToken cancellationToken);

    Task<SriAuthorizationResult> GetAuthorizationAsync(string accessKey, CancellationToken cancellationToken);
}
