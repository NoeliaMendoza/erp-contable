using System.Net.Mail;
using ERP.Modules.Sales.Dtos;
using ERP.Modules.Sales.Models;
using FluentValidation;

namespace ERP.Modules.Sales.Validators;

public sealed class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(x => x.IdentificationType)
            .Must(ParseIdentificationType)
            .WithMessage("El tipo debe ser Ruc, Cedula, Pasaporte o ConsumidorFinal.");

        RuleFor(x => x.IdentificationNumber).Cascade(CascadeMode.Stop)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Ingrese el número de identificación.")
            .Must((request, number) => ParseIdentificationType(request.IdentificationType)
                && ValidIdentificationNumber(request.IdentificationType, number))
            .WithMessage("El número de identificación no corresponde al formato acordado.");

        RuleFor(x => x.Name).Cascade(CascadeMode.Stop)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Ingrese el nombre.")
            .Must(n => n.Trim().Length <= Customer.NameMaxLength)
            .WithMessage($"El nombre admite como máximo {Customer.NameMaxLength} caracteres.");

        RuleFor(x => x.Email).Cascade(CascadeMode.Stop)
            .Must(email => string.IsNullOrWhiteSpace(email) || IsValidEmail(email))
            .WithMessage("Ingrese un email válido.")
            .Must(email => email is null || email.Trim().Length <= Customer.EmailMaxLength)
            .WithMessage($"El email admite como máximo {Customer.EmailMaxLength} caracteres.");

        RuleFor(x => x.Phone)
            .Must(phone => phone is null || phone.Trim().Length <= Customer.PhoneMaxLength)
            .WithMessage($"El teléfono admite como máximo {Customer.PhoneMaxLength} caracteres.");

        RuleFor(x => x.Address)
            .Must(address => address is null || address.Trim().Length <= Customer.AddressMaxLength)
            .WithMessage($"La dirección admite como máximo {Customer.AddressMaxLength} caracteres.");
    }

    public static bool ParseIdentificationType(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Enum.TryParse(value.Trim(), ignoreCase: true, out IdentificationType type)
        && Enum.IsDefined(type);

    public static bool ValidIdentificationNumber(string? typeValue, string? number)
    {
        if (!ParseIdentificationType(typeValue)
            || !Enum.TryParse(typeValue!.Trim(), ignoreCase: true, out IdentificationType type))
        {
            return false;
        }

        try
        {
            Customer.NormalizeIdentificationNumber(type, number);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return true;
        }

        try
        {
            _ = new MailAddress(email.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }
}
