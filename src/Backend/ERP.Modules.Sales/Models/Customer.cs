using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;

namespace ERP.Modules.Sales.Models;

public enum IdentificationType
{
    Ruc = 1,
    Cedula = 2,
    Pasaporte = 3,
    ConsumidorFinal = 4,
}

public sealed class Customer : ITenantOwned
{
    public const int IdentificationNumberMaxLength = 20;
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 320;
    public const int PhoneMaxLength = 30;
    public const int AddressMaxLength = 300;

    private Customer()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public IdentificationType IdentificationType { get; private set; }
    public string IdentificationNumber { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public static Customer Create(Guid id, Guid tenantId, CustomerData data, DateTimeOffset nowUtc, Guid? userId)
    {
        var customer = new Customer
        {
            Id = id,
            TenantId = tenantId,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = userId,
        };
        customer.Apply(data, nowUtc, userId);
        return customer;
    }

    public void Update(CustomerData data, DateTimeOffset nowUtc, Guid? userId)
    {
        Apply(data, nowUtc, userId);
    }

    public void SetActive(bool isActive, DateTimeOffset nowUtc, Guid? userId)
    {
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
        UpdatedBy = userId;
    }

    private void Apply(CustomerData data, DateTimeOffset nowUtc, Guid? userId)
    {
        if (!Enum.IsDefined(data.IdentificationType))
        {
            throw new DomainException("customer_identification_type_invalid", "Seleccione un tipo de identificación válido.");
        }

        IdentificationNumber = NormalizeIdentificationNumber(data.IdentificationType, data.IdentificationNumber);
        Name = NormalizeRequiredText(data.Name, NameMaxLength, "customer_name", "El nombre");
        Email = NormalizeOptionalText(data.Email, EmailMaxLength, "customer_email", "El email");
        Phone = NormalizeOptionalText(data.Phone, PhoneMaxLength, "customer_phone", "El teléfono");
        Address = NormalizeOptionalText(data.Address, AddressMaxLength, "customer_address", "La dirección");
        IdentificationType = data.IdentificationType;
        UpdatedAtUtc = nowUtc;
        UpdatedBy = userId;
    }

    public static string NormalizeIdentificationNumber(IdentificationType type, string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is 0 or > IdentificationNumberMaxLength)
        {
            throw new DomainException("customer_identification_invalid",
                $"El número de identificación admite entre 1 y {IdentificationNumberMaxLength} caracteres.");
        }

        if (type is IdentificationType.Ruc or IdentificationType.Cedula)
        {
            normalized = normalized.ToUpperInvariant();
            if (!normalized.All(char.IsAsciiDigit))
            {
                throw new DomainException("customer_identification_digits",
                    type == IdentificationType.Ruc
                        ? "El RUC debe contener solo dígitos."
                        : "La cédula debe contener solo dígitos.");
            }

            var expectedLength = type == IdentificationType.Ruc ? 13 : 10;
            if (normalized.Length != expectedLength)
            {
                throw new DomainException("customer_identification_length",
                    type == IdentificationType.Ruc
                        ? "El RUC debe tener 13 dígitos."
                        : "La cédula debe tener 10 dígitos.");
            }
        }
        else
        {
            normalized = normalized.ToUpperInvariant();
        }

        return normalized;
    }

    private static string NormalizeRequiredText(string? value, int maxLength, string code, string label)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > maxLength)
        {
            throw new DomainException(code + "_invalid", $"{label} debe tener entre 1 y {maxLength} caracteres.");
        }
        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength, string code, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }
        if (normalized.Length > maxLength)
        {
            throw new DomainException(code + "_invalid", $"{label} admite como máximo {maxLength} caracteres.");
        }
        return normalized;
    }
}

public sealed record CustomerData(
    IdentificationType IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Email,
    string? Phone,
    string? Address);
