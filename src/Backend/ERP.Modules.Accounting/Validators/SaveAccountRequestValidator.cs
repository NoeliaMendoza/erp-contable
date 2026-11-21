using ERP.Modules.Accounting.Dtos;
using ERP.Modules.Accounting.Models;
using FluentValidation;

namespace ERP.Modules.Accounting.Validators;

public sealed class SaveAccountRequestValidator : AbstractValidator<SaveAccountRequest>
{
    public SaveAccountRequestValidator()
    {
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop)
            .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Ingrese el código.")
            .Must(c => c.Trim().Length <= Account.CodeMaxLength).WithMessage($"El código admite como máximo {Account.CodeMaxLength} caracteres.")
            .Must(c => c.Trim().All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-'))
            .WithMessage("El código solo admite letras, números, punto, guion y guion bajo.");
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Ingrese el nombre.")
            .Must(n => n.Trim().Length <= Account.NameMaxLength).WithMessage($"El nombre admite como máximo {Account.NameMaxLength} caracteres.");
        RuleFor(x => x.Kind).Must(v => AccountValues.TryParse<AccountKind>(v, out _))
            .WithMessage("Seleccione Asset (activo), Liability (pasivo), Equity (patrimonio), Income (ingreso) o Expense (gasto).");
        RuleFor(x => x.NormalBalance).Must(v => AccountValues.TryParse<NormalBalance>(v, out _))
            .WithMessage("Seleccione Debit (deudora) o Credit (acreedora).");
        RuleFor(x => x.ParentId).Must(id => id != Guid.Empty).WithMessage("Seleccione una cuenta padre válida o ninguna.");
    }
}

internal static class AccountValues
{
    public static bool TryParse<T>(string? value, out T result)
        where T : struct, Enum
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && Enum.GetNames<T>().Any(n => string.Equals(n, value.Trim(), StringComparison.OrdinalIgnoreCase))
            && Enum.TryParse(value.Trim(), true, out result);
    }
}
