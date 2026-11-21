using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;

namespace ERP.Modules.Accounting.Models;

/// <summary>Cuenta del plan de una empresa. El código es texto y conserva ceros iniciales.</summary>
public sealed class Account : ITenantOwned
{
    public const int CodeMaxLength = 40;
    public const int NameMaxLength = 160;

    private Account()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public AccountKind Kind { get; private set; }

    public NormalBalance NormalBalance { get; private set; }

    public Guid? ParentId { get; private set; }

    public bool AllowsPosting { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Account Create(Guid id, Guid tenantId, AccountData data, DateTimeOffset nowUtc, Guid? userId)
    {
        var account = new Account { Id = id, TenantId = tenantId, IsActive = true, CreatedAtUtc = nowUtc, CreatedBy = userId };
        account.Apply(data);
        return account;
    }

    public void Update(AccountData data, DateTimeOffset nowUtc, Guid? userId)
    {
        Apply(data);
        Touch(nowUtc, userId);
    }

    public void SetActive(bool isActive, DateTimeOffset nowUtc, Guid? userId)
    {
        IsActive = isActive;
        Touch(nowUtc, userId);
    }

    public static string NormalizeCode(string? code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is 0 or > CodeMaxLength || !normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            throw new DomainException("account_code_invalid", $"El código admite de 1 a {CodeMaxLength} letras, números, puntos, guiones o guiones bajos.");
        }

        return normalized;
    }

    private void Apply(AccountData data)
    {
        // Validar todo antes de modificar la entidad.
        var code = NormalizeCode(data.Code);
        var name = (data.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > NameMaxLength)
        {
            throw new DomainException("account_name_invalid", $"El nombre admite de 1 a {NameMaxLength} caracteres.");
        }

        if (!Enum.IsDefined(data.Kind))
        {
            throw new DomainException("account_kind_invalid", "Seleccione un tipo de cuenta válido.");
        }

        if (!Enum.IsDefined(data.NormalBalance))
        {
            throw new DomainException("account_normal_balance_invalid", "Seleccione naturaleza deudora o acreedora.");
        }

        if (data.ParentId == Guid.Empty || data.ParentId == Id)
        {
            throw new DomainException("account_parent_invalid", "La cuenta padre debe ser válida y distinta de la cuenta actual.");
        }

        Code = code;
        Name = name;
        Kind = data.Kind;
        NormalBalance = data.NormalBalance;
        ParentId = data.ParentId;
        AllowsPosting = data.AllowsPosting;
    }

    private void Touch(DateTimeOffset nowUtc, Guid? userId)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = userId;
    }
}
