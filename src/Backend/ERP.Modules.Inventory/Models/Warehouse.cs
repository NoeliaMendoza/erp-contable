using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;

namespace ERP.Modules.Inventory.Models;

/// <summary>
/// Bodega de la empresa. Cada empresa tiene una sola bodega principal (<see cref="IsDefault"/>), que se crea
/// sola la primera vez que se necesita y no se puede desactivar. Código único por empresa.
/// </summary>
public sealed class Warehouse : ITenantOwned
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 80;
    public const string DefaultCode = "PRINCIPAL";
    public const string DefaultName = "Bodega principal";

    private Warehouse()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Warehouse Create(Guid id, Guid tenantId, string code, string name, bool isDefault, DateTimeOffset nowUtc, Guid? userId) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Code = NormalizeCode(code),
            Name = NormalizeName(name),
            IsDefault = isDefault,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = userId,
        };

    public void Rename(string name, DateTimeOffset nowUtc, Guid? userId)
    {
        Name = NormalizeName(name);
        Touch(nowUtc, userId);
    }

    public void SetActive(bool isActive, DateTimeOffset nowUtc, Guid? userId)
    {
        if (!isActive && IsDefault)
        {
            throw new DomainException("inventory_default_warehouse_required", "La bodega principal no se puede desactivar.");
        }

        IsActive = isActive;
        Touch(nowUtc, userId);
    }

    /// <summary>Mayúsculas y sin espacios alrededor; solo letras, números, guion y guion bajo.</summary>
    public static string NormalizeCode(string code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is 0 or > CodeMaxLength || !normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new DomainException("inventory_warehouse_code_invalid",
                $"El código de la bodega debe tener entre 1 y {CodeMaxLength} letras, números, guion o guion bajo.");
        }

        return normalized;
    }

    public static string NormalizeName(string name)
    {
        var normalized = string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length is 0 or > NameMaxLength)
        {
            throw new DomainException("inventory_warehouse_name_invalid", $"El nombre de la bodega debe tener entre 1 y {NameMaxLength} caracteres.");
        }

        return normalized;
    }

    private void Touch(DateTimeOffset nowUtc, Guid? userId)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = userId;
    }
}
