using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Tributario.Identification;

namespace ERP.Modules.Purchases.Models;

/// <summary>
/// Proveedor perteneciente a una empresa del ERP.
/// </summary>
public sealed class Supplier : ITenantOwned
{
    public const int IdentificationTypeMaxLength = 2;
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 30;
    public const int AddressMaxLength = 300;

    private Supplier()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string IdentificationType { get; private set; } = string.Empty;

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

    public static Supplier Create(
        Guid id,
        Guid tenantId,
        SupplierData data,
        DateTimeOffset nowUtc,
        Guid? userId)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new DomainException(
                "supplier_identity_invalid",
                "El proveedor y la empresa deben tener identificadores válidos.");
        }

        var supplier = new Supplier
        {
            Id = id,
            TenantId = tenantId,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = userId
        };

        supplier.ApplyData(data);

        return supplier;
    }

    public void Update(
        SupplierData data,
        DateTimeOffset nowUtc,
        Guid? userId)
    {
        ApplyData(data);
        Touch(nowUtc, userId);
    }

    public void SetActive(
        bool isActive,
        DateTimeOffset nowUtc,
        Guid? userId)
    {
        IsActive = isActive;
        Touch(nowUtc, userId);
    }

    private void ApplyData(SupplierData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var identification = EcuadorIdentificationCatalog.Validate(
            data.IdentificationType,
            data.IdentificationNumber,
            EcuadorIdentificationSubject.Supplier);
        if (!identification.IsValid)
        {
            throw new DomainException("supplier_identification_invalid", identification.Error!);
        }

        IdentificationType = identification.SriCode;
        IdentificationNumber = identification.NormalizedNumber;

        Name = NormalizeRequired(
            data.Name,
            NameMaxLength,
            "nombre");

        Email = NormalizeOptional(
            data.Email,
            EmailMaxLength,
            "correo electrónico");

        Phone = NormalizeOptional(
            data.Phone,
            PhoneMaxLength,
            "teléfono");

        Address = NormalizeOptional(
            data.Address,
            AddressMaxLength,
            "dirección");
    }

    private static string NormalizeRequired(
        string? value,
        int maxLength,
        string field)
    {
        var normalized = (value ?? string.Empty).Trim();

        if (normalized.Length is 0 || normalized.Length > maxLength)
        {
            throw new DomainException(
                "supplier_field_invalid",
                $"El campo {field} debe contener entre 1 y {maxLength} caracteres.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        int maxLength,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new DomainException(
                "supplier_field_invalid",
                $"El campo {field} admite como máximo {maxLength} caracteres.");
        }

        return normalized;
    }

    private void Touch(
        DateTimeOffset nowUtc,
        Guid? userId)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = userId;
    }
}
