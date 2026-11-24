using System.Text.RegularExpressions;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Inventory.Dtos;
using ERP.Modules.Inventory.Models;
using FluentValidation;

namespace ERP.Modules.Inventory.Validators;

/// <summary>
/// Validaciones de entrada. Los códigos se validan YA RECORTADOS, igual que se normalizan al guardar
/// (recortar espacios y pasar a mayúsculas), así " kg " es válido y se guarda como "KG".
/// </summary>
public sealed class SaveUnitRequestValidator : AbstractValidator<SaveUnitRequest>
{
    public SaveUnitRequestValidator()
    {
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop)
            .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Ingrese el código de la unidad.")
            .Must(c => c.Trim().Length <= Unit.CodeMaxLength).WithMessage($"El código admite como máximo {Unit.CodeMaxLength} caracteres.")
            .Must(c => CodePattern.IsCode(c)).WithMessage("El código solo admite letras, números, punto, guion y guion bajo.");
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Ingrese el nombre de la unidad.")
            .Must(n => n.Trim().Length <= Unit.NameMaxLength).WithMessage($"El nombre admite como máximo {Unit.NameMaxLength} caracteres.");
    }
}

public sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductRequestValidator()
    {
        RuleFor(x => x.Sku).Cascade(CascadeMode.Stop)
            .Must(s => !string.IsNullOrWhiteSpace(s)).WithMessage("Ingrese el SKU.")
            .Must(s => s.Trim().Length <= Product.SkuMaxLength).WithMessage($"El SKU admite como máximo {Product.SkuMaxLength} caracteres.")
            .Must(s => CodePattern.IsCode(s)).WithMessage("El SKU solo admite letras, números, punto, guion y guion bajo.");
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Ingrese el nombre.")
            .Must(n => n.Trim().Length <= Product.NameMaxLength).WithMessage($"El nombre admite como máximo {Product.NameMaxLength} caracteres.");
        RuleFor(x => x.Kind).Must(k => KindParser.TryParse(k, out _))
            .WithMessage("El tipo debe ser Product (producto) o Service (servicio).");
        RuleFor(x => x.UnitId).NotEmpty().WithMessage("Seleccione una unidad de medida.");
        RuleFor(x => x.Price).Cascade(CascadeMode.Stop)
            .GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo.")
            .Must(p => decimal.Round(p, Product.PriceScale) == p)
            .WithMessage($"El precio admite como máximo {Product.PriceScale} decimales.");
        RuleFor(x => x.VatTreatment).Must(v => VatTreatmentParser.TryParse(v, out _))
            .WithMessage("Seleccione el tratamiento de IVA: Standard, ZeroRated, Exempt o NotSubject.");
        RuleFor(x => x.Barcode).Cascade(CascadeMode.Stop)
            .Must(b => b!.Trim().Length <= Product.BarcodeMaxLength).WithMessage($"El código de barras admite como máximo {Product.BarcodeMaxLength} caracteres.")
            .Must(b => CodePattern.IsCode(b)).WithMessage("El código de barras solo admite letras, números, punto, guion y guion bajo.")
            .When(x => !string.IsNullOrWhiteSpace(x.Barcode));
        RuleFor(x => x.Description)
            .Must(d => d!.Trim().Length <= Product.DescriptionMaxLength).WithMessage($"La descripción admite como máximo {Product.DescriptionMaxLength} caracteres.")
            .When(x => x.Description is not null);
        RuleFor(x => x.Cost!.Value).Cascade(CascadeMode.Stop)
            .GreaterThanOrEqualTo(0).WithMessage("El costo no puede ser negativo.")
            .Must(c => decimal.Round(c, Product.PriceScale) == c).WithMessage($"El costo admite como máximo {Product.PriceScale} decimales.")
            .OverridePropertyName(nameof(SaveProductRequest.Cost))
            .When(x => x.Cost is not null);
        RuleFor(x => x.TracksInventory)
            .Must((request, tracks) => tracks != true || !KindParser.TryParse(request.Kind, out var kind) || kind != ProductKind.Service)
            .WithMessage("Un servicio no controla inventario.");
        RuleFor(x => x.MinStock!.Value).Cascade(CascadeMode.Stop)
            .GreaterThanOrEqualTo(0).WithMessage("El stock mínimo no puede ser negativo.")
            .Must(m => decimal.Round(m, Product.QuantityScale) == m).WithMessage($"El stock mínimo admite como máximo {Product.QuantityScale} decimales.")
            .OverridePropertyName(nameof(SaveProductRequest.MinStock))
            .When(x => x.MinStock is not null);
        RuleFor(x => x.MinStock)
            .Null().WithMessage("El stock mínimo solo aplica a artículos que controlan inventario.")
            .When(x => !ResolvesTracking(x));
    }

    /// <summary>Mismo valor por defecto que el dominio: producto controla inventario, servicio no.</summary>
    private static bool ResolvesTracking(SaveProductRequest request) =>
        request.TracksInventory ?? !(KindParser.TryParse(request.Kind, out var kind) && kind == ProductKind.Service);
}

public sealed class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator()
    {
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Ingrese el nombre de la categoría.")
            .Must(n => n.Trim().Length <= Category.NameMaxLength).WithMessage($"El nombre admite como máximo {Category.NameMaxLength} caracteres.");
    }
}

public sealed class CreateWarehouseRequestValidator : AbstractValidator<CreateWarehouseRequest>
{
    public CreateWarehouseRequestValidator()
    {
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop)
            .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Ingrese el código de la bodega.")
            .Must(c => c.Trim().Length <= Warehouse.CodeMaxLength).WithMessage($"El código admite como máximo {Warehouse.CodeMaxLength} caracteres.")
            .Must(c => c.Trim().All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
            .WithMessage("El código solo admite letras, números, guion y guion bajo.");
        RuleFor(x => x.Name).SetValidator(new WarehouseNameValidator());
    }
}

public sealed class RenameWarehouseRequestValidator : AbstractValidator<RenameWarehouseRequest>
{
    public RenameWarehouseRequestValidator() => RuleFor(x => x.Name).SetValidator(new WarehouseNameValidator());
}

internal sealed class WarehouseNameValidator : AbstractValidator<string>
{
    public WarehouseNameValidator()
    {
        RuleFor(n => n).Cascade(CascadeMode.Stop)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Ingrese el nombre de la bodega.")
            .Must(n => n.Trim().Length <= Warehouse.NameMaxLength).WithMessage($"El nombre admite como máximo {Warehouse.NameMaxLength} caracteres.");
    }
}

internal static partial class CodePattern
{
    public static bool IsCode(string? value) => value is not null && Allowed().IsMatch(value.Trim());

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex Allowed();
}

internal static class VatTreatmentParser
{
    public static bool TryParse(string? value, out VatTreatment treatment)
    {
        treatment = default;
        return !string.IsNullOrWhiteSpace(value)
            && !int.TryParse(value, out _)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out treatment)
            && Enum.IsDefined(treatment);
    }
}

internal static class KindParser
{
    public static bool TryParse(string? value, out ProductKind kind)
    {
        kind = default;
        return !string.IsNullOrWhiteSpace(value)
            && !int.TryParse(value, out _)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out kind)
            && Enum.IsDefined(kind);
    }
}
