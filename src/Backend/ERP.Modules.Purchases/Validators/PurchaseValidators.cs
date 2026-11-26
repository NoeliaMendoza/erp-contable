using ERP.Modules.Purchases.Dtos;
using FluentValidation;

namespace ERP.Modules.Purchases.Validators;

public sealed class RegisterPurchaseRequestValidator : AbstractValidator<RegisterPurchaseRequest>
{
    public RegisterPurchaseRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.Lines).Cascade(CascadeMode.Stop).NotEmpty()
            .Must(lines => lines.All(line => line is not null))
            .WithMessage("Cada línea debe incluir un artículo.")
            .Must(lines => lines.Select(line => line.ProductId).Distinct().Count() == lines.Count)
            .WithMessage("Cada artículo debe aparecer una sola vez en la compra.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ProductId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0).Must(value => decimal.Round(value, 4) == value)
                .WithMessage("La cantidad debe ser mayor que cero y admite máximo 4 decimales.");
            line.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).Must(value => decimal.Round(value, 4) == value)
                .WithMessage("El costo unitario no puede ser negativo y admite máximo 4 decimales.");
        });
    }
}
