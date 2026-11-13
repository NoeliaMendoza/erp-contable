namespace ERP.Modules.Accounting.Contracts;

/// <summary>
/// Línea solicitada por un módulo origen. Exactamente uno de Debit/Credit debe ser &gt; 0.
/// Importes en USD (moneda contable base) con la escala definida por Contabilidad.
/// </summary>
public sealed record PostingLine(string AccountCode, decimal Debit, decimal Credit, string? Memo = null);

/// <summary>
/// Solicitud de contabilización de un documento de negocio (factura, compra, pago, movimiento...).
/// </summary>
/// <param name="IdempotencyKey">Clave estable del origen (p. ej. <c>sales:invoice:{id}</c>). Reintentos con la misma clave no duplican asientos.</param>
/// <param name="SourceModule">Módulo solicitante (sales, purchases, inventory...).</param>
/// <param name="SourceDocumentType">Tipo de documento origen, para trazabilidad documento → asiento.</param>
/// <param name="SourceDocumentId">Identificador del documento origen.</param>
/// <param name="AccountingDate">Fecha contable (fecha local Ecuador, no instante UTC).</param>
public sealed record PostingRequest(
    string IdempotencyKey,
    string SourceModule,
    string SourceDocumentType,
    Guid SourceDocumentId,
    DateOnly AccountingDate,
    string Description,
    IReadOnlyList<PostingLine> Lines);

/// <param name="AlreadyProcessed">true si la clave de idempotencia ya existía y se devolvió el asiento original.</param>
public sealed record PostingResult(Guid JournalEntryId, bool AlreadyProcessed);

/// <summary>
/// Único camino para que otro módulo genere asientos. Ningún módulo inserta en tablas de Contabilidad.
/// La implementación (Integrante 2) valida partida doble, período abierto, cuentas válidas del tenant
/// e idempotencia, y participa en la transacción del caso de uso que la invoca.
/// Estado: <b>contrato definido, implementación pendiente</b>.
/// </summary>
public interface IAccountingPostingService
{
    Task<PostingResult> PostAsync(PostingRequest request, CancellationToken cancellationToken);

    /// <summary>Reverso auditable de un asiento contabilizado (nunca se elimina).</summary>
    Task<PostingResult> ReverseAsync(Guid journalEntryId, DateOnly reversalDate, string reason, string idempotencyKey, CancellationToken cancellationToken);
}
