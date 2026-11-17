using System.Text.RegularExpressions;
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
    }
}

internal static partial class CodePattern
{
    public static bool IsCode(string? value) => value is not null && Allowed().IsMatch(value.Trim());

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex Allowed();
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
