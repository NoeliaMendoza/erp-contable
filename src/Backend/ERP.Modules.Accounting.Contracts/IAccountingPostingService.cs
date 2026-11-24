namespace ERP.Modules.Accounting.Contracts;

/// <summary>
/// Línea solicitada por un módulo origen. Exactamente uno de Debit/Credit debe ser &gt; 0.
/// Importes decimal en USD. La propuesta inicial conserva la escala provisional de dos decimales:
/// el servicio rechaza una escala mayor y no redondea para cuadrar un asiento.
/// </summary>
/// <param name="AccountCode">Código del plan de la empresa activa; no es un ID ni una cuenta universal entre empresas.</param>
/// <param name="Debit">Debe no negativo. Exactamente uno de Debit/Credit es positivo.</param>
/// <param name="Credit">Haber no negativo. Exactamente uno de Debit/Credit es positivo.</param>
/// <param name="Memo">Descripción opcional de la línea.</param>
public sealed record PostingLine(string AccountCode, decimal Debit, decimal Credit, string? Memo = null);

/// <summary>
/// Solicitud de contabilización de un documento de negocio (factura, compra, pago, movimiento...).
/// La empresa y el actor se obtienen del ITenantContext confiable del servidor, nunca de un campo editable del DTO.
/// </summary>
/// <param name="IdempotencyKey">Clave estable por empresa y operación (p. ej. <c>sales:invoice:{id}</c>). El emisor la conserva en cada reintento.</param>
/// <param name="SourceModule">Módulo solicitante (sales, purchases, inventory...).</param>
/// <param name="SourceDocumentType">Tipo de documento origen, para trazabilidad documento → asiento.</param>
/// <param name="SourceDocumentId">Identificador del documento origen.</param>
/// <param name="AccountingDate">Fecha contable (fecha local Ecuador, no instante UTC).</param>
/// <param name="Description">Descripción requerida de la operación.</param>
/// <param name="Lines">Al menos dos líneas; suma del debe igual a suma del haber y mayor que cero.</param>
/// <remarks>
/// Compras/Ventas resuelven y conservan el desglose fiscal del documento: el asiento recibe importes finales,
/// incluidas las líneas de IVA cuando corresponda. Contabilidad no asume una tarifa ni recalcula el impuesto.
/// El IVA de compra solo se lleva a su cuenta recuperable cuando el origen ha determinado ese tratamiento.
/// </remarks>
public sealed record PostingRequest(
    string IdempotencyKey,
    string SourceModule,
    string SourceDocumentType,
    Guid SourceDocumentId,
    DateOnly AccountingDate,
    string Description,
    IReadOnlyList<PostingLine> Lines);

/// <summary>Resultado de la solicitud contable; no sustituye el commit global del caso de uso llamador.</summary>
/// <param name="JournalEntryId">ID del asiento en la empresa activa; se conserva al repetir la misma solicitud.</param>
/// <param name="AlreadyProcessed">true si la clave ya existía con el mismo contenido y se devolvió el asiento original.</param>
public sealed record PostingResult(Guid JournalEntryId, bool AlreadyProcessed);

/// <summary>
/// Único camino para que otro módulo genere asientos. Ningún módulo inserta en tablas de Contabilidad.
/// La implementación (Integrante 2) valida partida doble, período abierto, cuentas válidas del tenant
/// e idempotencia, y participa en la transacción del caso de uso que la invoca.
/// Estado: <b>firmas y acuerdo operativo #12 definidos; contabilización implementada en #17</b>.
/// Quedan fuera de esa entrega el período contable (<c>period_closed</c> no se evalúa todavía, #36),
/// <see cref="ReverseAsync"/> (#34) y la transacción compartida entre módulos (PLAT-03, #35).
/// </summary>
/// <remarks>
/// ACUERDO-02 se documenta en docs/contratos-modulos.md y docs/modulos/acuerdo02-propuesta.md.
/// Sin IVA, la propuesta define compra de dos líneas y venta de cuatro líneas (ingreso y costo juntos).
/// Cuando aplica IVA se agregan sus líneas en el mismo asiento y con la misma clave del documento.
/// Misma empresa/clave/contenido devuelve el asiento existente; misma clave con datos distintos es conflicto.
/// Compartir la base no basta para atomicidad: el coordinador debe compartir conexión y transacción entre módulos.
/// Las excepciones se propagan para revertir el caso de uso; no se devuelve éxito parcial.
/// <see cref="PostAsync"/> cumple estas reglas y cuenta con pruebas de integración contra PostgreSQL real.
/// El consumidor debe enviar <b>códigos del plan de cuentas de la empresa activa</b>, no nombres de cuenta.
/// </remarks>
public interface IAccountingPostingService
{
    Task<PostingResult> PostAsync(PostingRequest request, CancellationToken cancellationToken);

    /// <summary>Reverso auditable de un asiento contabilizado (nunca se elimina).</summary>
    Task<PostingResult> ReverseAsync(Guid journalEntryId, DateOnly reversalDate, string reason, string idempotencyKey, CancellationToken cancellationToken);
}
