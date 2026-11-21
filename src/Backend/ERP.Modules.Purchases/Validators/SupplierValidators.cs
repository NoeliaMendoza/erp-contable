
using ERP.Modules.Purchases.Dtos;
using ERP.Modules.Purchases.Models;
using FluentValidation;
using ERP.Tributario.Identification;

namespace ERP.Modules.Purchases.Validators;

/// <summary>
/// Valida los datos recibidos por la API de proveedores.
/// </summary>
public sealed class SaveSupplierRequestValidator
    : AbstractValidator<SaveSupplierRequest>
{
    public SaveSupplierRequestValidator()
    {
        RuleFor(x => x.IdentificationType)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(type => EcuadorIdentificationCatalog.TryGetType(type, out var definition)
                && definition.AvailableForSuppliers)
            .WithMessage("Seleccione un tipo de identificación permitido para proveedores.");

        RuleFor(x => x.IdentificationNumber)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Ingrese el número de identificación.")
            .Must((request, value) => EcuadorIdentificationCatalog.Validate(
                request.IdentificationType,
                value,
                EcuadorIdentificationSubject.Supplier).IsValid)
            .WithMessage((request, value) => EcuadorIdentificationCatalog.Validate(
                request.IdentificationType,
                value,
                EcuadorIdentificationSubject.Supplier).Error ?? "Identificación inválida.");

        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Ingrese el nombre o razón social.")
            .Must(x => x.Trim().Length <= Supplier.NameMaxLength)
            .WithMessage("El nombre supera la longitud permitida.");

        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(Supplier.EmailMaxLength)
            .WithMessage("El correo electrónico es demasiado largo.")
            .EmailAddress()
            .WithMessage("Ingrese un correo electrónico válido.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Phone)
            .Must(x => x is null ||
                x.Trim().Length <= Supplier.PhoneMaxLength)
            .WithMessage("El teléfono supera la longitud permitida.");

        RuleFor(x => x.Address)
            .Must(x => x is null ||
                x.Trim().Length <= Supplier.AddressMaxLength)
            .WithMessage("La dirección supera la longitud permitida.");
    }
}
