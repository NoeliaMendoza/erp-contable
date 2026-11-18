using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;

namespace ERP.Modules.Inventory.Models;

/// <summary>
/// Categoría o línea del catálogo (p. ej. Papelería, Bebidas, Servicios técnicos).
/// Nombre único por empresa sin distinguir mayúsculas: "Papelería" y "PAPELERÍA" son la misma.
/// </summary>
public sealed class Category : ITenantOwned
{
    public const int NameMaxLength = 80;

    private Category()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Category Create(Guid id, Guid tenantId, string name, DateTimeOffset nowUtc, Guid? userId) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Name = NormalizeName(name),
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
        IsActive = isActive;
        Touch(nowUtc, userId);
    }

    /// <summary>Recorta y colapsa espacios internos repetidos; conserva mayúsculas/minúsculas tal como se escribió.</summary>
    public static string NormalizeName(string name)
    {
        var normalized = string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length is 0 or > NameMaxLength)
        {
            throw new DomainException("category_name_invalid", $"El nombre de la categoría debe tener entre 1 y {NameMaxLength} caracteres.");
        }

        return normalized;
    }

    private void Touch(DateTimeOffset nowUtc, Guid? userId)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = userId;
    }
}
