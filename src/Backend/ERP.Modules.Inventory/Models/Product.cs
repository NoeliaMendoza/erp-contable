using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Modules.Inventory.Contracts;

namespace ERP.Modules.Inventory.Models;

public enum ProductKind
{
    Product = 1,
    Service = 2,
}

/// <summary>Datos editables de la ficha del artículo (lo que llega del formulario).</summary>
/// <param name="TracksInventory">null = valor por defecto según el tipo: producto sí, servicio no.</param>
public sealed record ProductData(
    string Sku,
    string Name,
    ProductKind Kind,
    Guid UnitId,
    decimal Price,
    VatTreatment VatTreatment,
    Guid? CategoryId = null,
    string? Barcode = null,
    string? Description = null,
    decimal? Cost = null,
    bool? TracksInventory = null,
    decimal? MinStock = null);

/// <summary>
/// Artículo del catálogo: producto (mercadería) o servicio.
/// <list type="bullet">
/// <item>SKU: texto, MAYÚSCULAS sin espacios al inicio/fin, único por empresa.</item>
/// <item>Unidad obligatoria también para servicios (p. ej. HORA, SERV): política uniforme.</item>
/// <item>Precio de venta y costo referencial en USD, <c>decimal</c> con máximo 4 decimales, no negativos, sin redondeo silencioso.</item>
/// <item>Tratamiento de IVA obligatorio; el porcentaje se resuelve al facturar, nunca se guarda aquí.</item>
/// <item>Un servicio nunca controla inventario. El stock mínimo solo aplica si se controla inventario.</item>
/// <item>Este catálogo NO calcula existencias: el stock llegará con los movimientos (INV-03).</item>
/// </list>
/// </summary>
public sealed class Product : ITenantOwned
{
    public const int SkuMaxLength = 40;
    public const int NameMaxLength = 200;

    /// <summary>Igual que el código auxiliar de la factura electrónica.</summary>
    public const int BarcodeMaxLength = 25;

    /// <summary>Igual que el campo "descripción" del detalle de la factura electrónica.</summary>
    public const int DescriptionMaxLength = 300;

    public const int PriceScale = 4;
    public const int QuantityScale = 4;

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

    public VatTreatment VatTreatment { get; private set; }

    public Guid? CategoryId { get; private set; }

    public string? Barcode { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Costo referencial para ver el margen. El costo real lo dará el kardex (INV-05).</summary>
    public decimal? Cost { get; private set; }

    public bool TracksInventory { get; private set; }

    public decimal? MinStock { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Product Create(Guid id, Guid tenantId, ProductData data, DateTimeOffset nowUtc, Guid? userId)
    {
        var product = new Product
        {
            Id = id,
            TenantId = tenantId,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = userId,
        };
        product.Apply(data);
        return product;
    }

    public void Update(ProductData data, DateTimeOffset nowUtc, Guid? userId)
    {
        Apply(data);
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

    /// <summary>Vacío o solo espacios = sin código de barras (null). Se recorta y se pasa a mayúsculas.</summary>
    public static string? NormalizeBarcode(string? barcode)
    {
        var normalized = barcode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > BarcodeMaxLength)
        {
            throw new DomainException("product_barcode_invalid", $"El código de barras admite como máximo {BarcodeMaxLength} caracteres.");
        }

        return normalized;
    }

    private void Apply(ProductData data)
    {
        var trimmedName = (data.Name ?? string.Empty).Trim();
        if (trimmedName.Length is 0 or > NameMaxLength)
        {
            throw new DomainException("product_name_invalid", $"El nombre debe tener entre 1 y {NameMaxLength} caracteres.");
        }

        if (!Enum.IsDefined(data.Kind))
        {
            throw new DomainException("product_kind_invalid", "El tipo debe ser Product (producto) o Service (servicio).");
        }

        if (!Enum.IsDefined(data.VatTreatment))
        {
            throw new DomainException("product_vat_invalid", "Seleccione el tratamiento de IVA del artículo.");
        }

        if (data.UnitId == Guid.Empty)
        {
            throw new DomainException("product_unit_required", "Seleccione una unidad de medida.");
        }

        RequireAmount(data.Price, "product_price", "El precio", PriceScale);
        if (data.Cost is { } cost)
        {
            RequireAmount(cost, "product_cost", "El costo", PriceScale);
        }

        var tracksInventory = data.TracksInventory ?? data.Kind == ProductKind.Product;
        if (tracksInventory && data.Kind == ProductKind.Service)
        {
            throw new DomainException("product_service_tracks_inventory", "Un servicio no controla inventario.");
        }

        if (data.MinStock is { } minStock)
        {
            if (!tracksInventory)
            {
                throw new DomainException("product_min_stock_without_tracking",
                    "El stock mínimo solo aplica a artículos que controlan inventario.");
            }

            RequireAmount(minStock, "product_min_stock", "El stock mínimo", QuantityScale);
        }

        var description = data.Description?.Trim();
        if (description?.Length > DescriptionMaxLength)
        {
            throw new DomainException("product_description_invalid", $"La descripción admite como máximo {DescriptionMaxLength} caracteres.");
        }

        Sku = NormalizeSku(data.Sku);
        Name = trimmedName;
        Kind = data.Kind;
        UnitId = data.UnitId;
        Price = data.Price;
        VatTreatment = data.VatTreatment;
        CategoryId = data.CategoryId == Guid.Empty ? null : data.CategoryId;
        Barcode = NormalizeBarcode(data.Barcode);
        Description = string.IsNullOrEmpty(description) ? null : description;
        Cost = data.Cost;
        TracksInventory = tracksInventory;
        MinStock = data.MinStock;
    }

    private static void RequireAmount(decimal value, string code, string label, int scale)
    {
        if (value < 0)
        {
            throw new DomainException(code + "_negative", $"{label} no puede ser negativo.");
        }

        if (decimal.Round(value, scale) != value)
        {
            throw new DomainException(code + "_scale", $"{label} admite como máximo {scale} decimales.");
        }
    }
}
