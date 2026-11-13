using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;

namespace ERP.Modules.Accounting.Models;

public enum JournalEntryStatus
{
    Draft = 0,
    Posted = 1,
    Reversed = 2,
}

/// <summary>Política monetaria provisional (USD, 2 decimales). Ver docs/decisiones-pendientes.md D-05.</summary>
public static class AccountingMoney
{
    public const int Scale = 2;

    public static bool HasValidScale(decimal amount) => decimal.Round(amount, Scale) == amount;
}

public sealed record JournalLine(int LineNumber, Guid AccountId, decimal Debit, decimal Credit, string? Memo);

/// <summary>
/// Asiento contable. Invariantes:
/// <list type="bullet">
/// <item>Solo se modifica en borrador.</item>
/// <item>Cada línea tiene debe XOR haber, no negativos, escala USD.</item>
/// <item>Para contabilizar: al menos 2 líneas, suma(debe) = suma(haber) &gt; 0 y período abierto.</item>
/// <item>Un asiento contabilizado no se elimina ni edita: se reversa con otro asiento auditable.</item>
/// </list>
/// </summary>
public sealed class JournalEntry : ITenantOwned
{
    private readonly List<JournalLine> _lines = [];

    private JournalEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public DateOnly AccountingDate { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public JournalEntryStatus Status { get; private set; }

    public string? IdempotencyKey { get; private set; }

    public Guid? ReversalOfId { get; private set; }

    public Guid? ReversedById { get; private set; }

    public DateTimeOffset? PostedAtUtc { get; private set; }

    public IReadOnlyList<JournalLine> Lines => _lines;

    public decimal TotalDebit => _lines.Sum(l => l.Debit);

    public decimal TotalCredit => _lines.Sum(l => l.Credit);

    public bool IsBalanced => TotalDebit == TotalCredit && TotalDebit > 0;

    public static JournalEntry CreateDraft(Guid id, Guid tenantId, DateOnly accountingDate, string description, string? idempotencyKey = null)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainException("journal_description_required", "El asiento requiere una descripción.");
        }

        return new JournalEntry
        {
            Id = id,
            TenantId = tenantId,
            AccountingDate = accountingDate,
            Description = description.Trim(),
            Status = JournalEntryStatus.Draft,
            IdempotencyKey = idempotencyKey,
        };
    }

    public void AddLine(Guid accountId, decimal debit, decimal credit, string? memo = null)
    {
        EnsureDraft();

        if (debit < 0 || credit < 0)
        {
            throw new DomainException("journal_line_negative", "Debe y haber no pueden ser negativos.");
        }

        if ((debit > 0) == (credit > 0))
        {
            throw new DomainException("journal_line_side", "Cada línea debe tener importe en debe o en haber, no en ambos ni en ninguno.");
        }

        if (!AccountingMoney.HasValidScale(debit) || !AccountingMoney.HasValidScale(credit))
        {
            throw new DomainException("journal_line_scale", $"Los importes admiten como máximo {AccountingMoney.Scale} decimales.");
        }

        _lines.Add(new JournalLine(_lines.Count + 1, accountId, debit, credit, memo));
    }

    /// <param name="isPeriodOpen">Resultado de consultar el período contable del tenant para la fecha del asiento.</param>
    public void Post(DateTimeOffset nowUtc, bool isPeriodOpen)
    {
        EnsureDraft();

        if (!isPeriodOpen)
        {
            throw new DomainException("period_closed", $"El período contable de {AccountingDate:yyyy-MM-dd} está cerrado.");
        }

        if (_lines.Count < 2)
        {
            throw new DomainException("journal_min_lines", "Un asiento requiere al menos dos líneas.");
        }

        if (!IsBalanced)
        {
            throw new DomainException(
                "journal_unbalanced",
                $"Partida doble incumplida: debe {TotalDebit:0.00} ≠ haber {TotalCredit:0.00}.");
        }

        Status = JournalEntryStatus.Posted;
        PostedAtUtc = nowUtc;
    }

    /// <summary>Crea el asiento de reverso (líneas invertidas, en borrador) y marca este como reversado.</summary>
    public JournalEntry CreateReversal(Guid reversalId, DateOnly reversalDate, string reason)
    {
        if (Status != JournalEntryStatus.Posted)
        {
            throw new DomainException("journal_not_posted", "Solo se pueden reversar asientos contabilizados y no reversados.");
        }

        var reversal = CreateDraft(reversalId, TenantId, reversalDate, $"Reverso de {Id}: {reason}");
        reversal.ReversalOfId = Id;
        foreach (var line in _lines)
        {
            reversal.AddLine(line.AccountId, line.Credit, line.Debit, line.Memo);
        }

        Status = JournalEntryStatus.Reversed;
        ReversedById = reversalId;
        return reversal;
    }

    private void EnsureDraft()
    {
        if (Status != JournalEntryStatus.Draft)
        {
            throw new DomainException("journal_immutable", "Un asiento contabilizado o reversado no puede modificarse.");
        }
    }
}
