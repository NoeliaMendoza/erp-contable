using ERP.BuildingBlocks.Tenancy;

namespace ERP.Modules.Accounting.Models;

public sealed class JournalEntryEntity : ITenantOwned
{
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

public sealed class JournalEntryLineEntity : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid JournalEntryId { get; private set; }
    public string AccountCode { get; private set; } = string.Empty;
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public string? Memo { get; private set; }

    public static JournalEntryLineEntity Create(Guid tenantId, Guid entryId, string accountCode, decimal debit, decimal credit, string? memo) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        JournalEntryId = entryId,
        AccountCode = accountCode,
        Debit = debit,
        Credit = credit,
        Memo = memo,
    };
}
