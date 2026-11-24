using ERP.BuildingBlocks.Tenancy;

namespace ERP.Modules.Accounting.Models;

/// <summary>
/// Asiento contabilizado y persistido. Es inmutable: la base revoca UPDATE y DELETE al rol de la
/// aplicación (ver <see cref="Data.AccountingDatabaseSecurity"/>), de modo que una corrección solo
/// puede expresarse como un reverso auditable y nunca como una edición o un borrado.
/// Conserva la trazabilidad al documento origen (módulo, tipo e ID) y la clave de idempotencia.
/// </summary>
public sealed class JournalEntryEntity : ITenantOwned
{
    public const int SourceMaxLength = 40;
    public const int IdempotencyKeyMaxLength = 200;
    public const int DescriptionMaxLength = 300;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateOnly AccountingDate { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string SourceModule { get; private set; } = string.Empty;
    public string SourceDocumentType { get; private set; } = string.Empty;
    public Guid SourceDocumentId { get; private set; }
    public JournalEntryStatus Status { get; private set; }
    public DateTimeOffset PostedAtUtc { get; private set; }

    public static JournalEntryEntity Create(Guid tenantId, Guid id, DateOnly date, string description,
        string idempotencyKey, string sourceModule, string sourceDocumentType, Guid sourceDocumentId,
        DateTimeOffset nowUtc) => new()
        {
            Id = id,
            TenantId = tenantId,
            AccountingDate = date,
            Description = description,
            IdempotencyKey = idempotencyKey,
            SourceModule = sourceModule,
            SourceDocumentType = sourceDocumentType,
            SourceDocumentId = sourceDocumentId,
            Status = JournalEntryStatus.Posted,
            PostedAtUtc = nowUtc,
        };
}

/// <summary>
/// Línea del asiento. <see cref="AccountId"/> es clave foránea compuesta con la empresa hacia el plan
/// de cuentas, por lo que la base rechaza una cuenta inexistente o de otra empresa aunque el servicio
/// fallara. <see cref="AccountCode"/> conserva el código vigente al contabilizar, para que el libro
/// diario siga siendo legible si la cuenta se renombra después.
/// </summary>
public sealed class JournalEntryLineEntity : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid JournalEntryId { get; private set; }

    /// <summary>Posición de la línea dentro del asiento, empezando en 1. El orden es parte del contenido idempotente.</summary>
    public int LineNumber { get; private set; }

    public Guid AccountId { get; private set; }
    public string AccountCode { get; private set; } = string.Empty;
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public string? Memo { get; private set; }

    public static JournalEntryLineEntity Create(Guid tenantId, Guid entryId, int lineNumber,
        Guid accountId, string accountCode, decimal debit, decimal credit, string? memo) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            JournalEntryId = entryId,
            LineNumber = lineNumber,
            AccountId = accountId,
            AccountCode = accountCode,
            Debit = debit,
            Credit = credit,
            Memo = memo,
        };
}
