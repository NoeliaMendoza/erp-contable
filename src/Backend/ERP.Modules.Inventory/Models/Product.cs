using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;

namespace ERP.Modules.Inventory.Models;

public enum ProductKind
{
    Product = 1,
    Service = 2,
}

/// <summary>
/// Artículo del catálogo: producto (mercadería) o servicio.
/// <list type="bullet">
/// <item>SKU: texto, MAYÚSCULAS sin espacios al inicio/fin, único por empresa.</item>
/// <item>Unidad obligatoria también para servicios (p. ej. HORA, SERV): política uniforme.</item>
/// <item>Precio de venta en USD, <c>decimal</c> con máximo 4 decimales, no negativo, sin redondeo silencioso.</item>
/// <item>Este catálogo NO calcula existencias: el stock llegará con los movimientos (INV-03).</item>
/// </list>
/// </summary>
public sealed class Product : ITenantOwned
{
    public const int SkuMaxLength = 40;
    public const int NameMaxLength = 200;
    public const int PriceScale = 4;

    private Product()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public ProductKind Kind { get; private set; }

    public Guid UnitId { get; private set; }

    public decimal Price { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Product Create(
        Guid id, Guid tenantId, string sku, string name, ProductKind kind, Guid unitId, decimal price, DateTimeOffset nowUtc, Guid? userId)
    {
        var product = new Product
        {
            Id = id,
            TenantId = tenantId,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = userId,
        };
        product.Apply(sku, name, kind, unitId, price);
        return product;
    }

    public void Update(string sku, string name, ProductKind kind, Guid unitId, decimal price, DateTimeOffset nowUtc, Guid? userId)
    {
        Apply(sku, name, kind, unitId, price);
        UpdatedAtUtc = nowUtc;
        UpdatedBy = userId;
    }

    public void SetActive(bool isActive, DateTimeOffset nowUtc, Guid? userId)
    {
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
        UpdatedBy = userId;
    }

    public static string NormalizeSku(string sku)
    {
        var normalized = (sku ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is 0 or > SkuMaxLength)
        {
            throw new DomainException("product_sku_invalid", $"El SKU debe tener entre 1 y {SkuMaxLength} caracteres.");
        }

        return normalized;
    }

    private void Apply(string sku, string name, ProductKind kind, Guid unitId, decimal price)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length is 0 or > NameMaxLength)
        {
            throw new DomainException("product_name_invalid", $"El nombre debe tener entre 1 y {NameMaxLength} caracteres.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException("product_kind_invalid", "El tipo debe ser Product (producto) o Service (servicio).");
        }

        if (unitId == Guid.Empty)
        {
            throw new DomainException("product_unit_required", "Seleccione una unidad de medida.");
        }

        if (price < 0)
        {
            throw new DomainException("product_price_negative", "El precio no puede ser negativo.");
        }

        if (decimal.Round(price, PriceScale) != price)
        {
            throw new DomainException("product_price_scale", $"El precio admite como máximo {PriceScale} decimales.");
        }

        Sku = NormalizeSku(sku);
        Name = trimmedName;
        Kind = kind;
        UnitId = unitId;
        Price = price;
    }
}
