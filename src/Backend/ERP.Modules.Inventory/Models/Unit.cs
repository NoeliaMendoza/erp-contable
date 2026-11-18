using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;

namespace ERP.Modules.Inventory.Models;

/// <summary>
/// Unidad de medida de la empresa (p. ej. UND, KG, HORA).
/// Código: texto, se guarda en MAYÚSCULAS sin espacios al inicio/fin; único por empresa.
/// </summary>
public sealed class Unit : ITenantOwned
{
    public const int CodeMaxLength = 10;
    public const int NameMaxLength = 60;

    private Unit()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Unit Create(Guid id, Guid tenantId, string code, string name, DateTimeOffset nowUtc, Guid? userId) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Code = NormalizeCode(code),
            Name = RequireName(name),
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = userId,
        };

    public void Update(string code, string name, DateTimeOffset nowUtc, Guid? userId)
    {
        Code = NormalizeCode(code);
        Name = RequireName(name);
        Touch(nowUtc, userId);
    }

    public void SetActive(bool isActive, DateTimeOffset nowUtc, Guid? userId)
    {
        IsActive = isActive;
        Touch(nowUtc, userId);
    }

    /// <summary>Regla de normalización documentada: recortar espacios y pasar a mayúsculas invariantes.</summary>
    public static string NormalizeCode(string code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is 0 or > CodeMaxLength)
        {
            throw new DomainException("unit_code_invalid", $"El código de unidad debe tener entre 1 y {CodeMaxLength} caracteres.");
        }

        return normalized;
    }

    private static string RequireName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            throw new DomainException("unit_name_invalid", $"El nombre de la unidad debe tener entre 1 y {NameMaxLength} caracteres.");
        }

        return trimmed;
    }

    private void Touch(DateTimeOffset nowUtc, Guid? userId)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = userId;
    }
}
