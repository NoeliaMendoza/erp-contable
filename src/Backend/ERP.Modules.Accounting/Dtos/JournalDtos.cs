namespace ERP.Modules.Accounting.Dtos;

// El libro diario siempre es de la empresa activa: la empresa viene de la sesión, nunca del cuerpo ni del query.

/// <summary>Línea del asiento con el código y el nombre vigentes de la cuenta del plan.</summary>
public sealed record JournalLineResponse(
    int LineNumber, Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit, string? Memo);

/// <summary>
/// Asiento del libro diario. Incluye el documento origen para auditar documento → asiento
/// y los totales de debe y haber, que siempre coinciden.
/// </summary>
public sealed record JournalEntryResponse(
    Guid Id,
    DateOnly AccountingDate,
    string Description,
    string Status,
    string SourceModule,
    string SourceDocumentType,
    Guid SourceDocumentId,
    DateTimeOffset PostedAtUtc,
    decimal TotalDebit,
    decimal TotalCredit,
    IReadOnlyList<JournalLineResponse> Lines);
