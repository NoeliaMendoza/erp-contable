using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using ERP.Tributario.Identification;

namespace ERP.Modules.Platform.Models;

/// <summary>Sucursal física u operativa del tenant. No es lo mismo que establecimiento SRI.</summary>
public sealed class Branch : ITenantOwned
{
    private Branch()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Address { get; private set; }

    public string? City { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public static Branch Create(Guid id, Guid tenantId, string code, string name, string? address, string? city, DateTimeOffset nowUtc, Guid? createdBy) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Address = address?.Trim(),
            City = city?.Trim(),
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = createdBy,
        };
}

/// <summary>
/// Contribuyente (empresa legal identificada por RUC) administrado por el tenant.
/// Si un tenant puede tener más de un RUC está pendiente (docs/decisiones-pendientes.md, D-01);
/// el esquema lo permite, la regla de negocio aún no está decidida.
/// Los atributos tributarios tienen vigencia: un cambio de régimen se registra como nueva versión (pendiente).
/// </summary>
public sealed class Taxpayer : ITenantOwned
{
    private Taxpayer()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Ruc { get; private set; } = string.Empty;

    public string LegalName { get; private set; } = string.Empty;

    public string? TradeName { get; private set; }

    public TaxpayerKind Kind { get; private set; }

    /// <summary>Obligado a llevar contabilidad (dato declarado; verificar contra el RUC en el SRI).</summary>
    public bool KeepsAccounting { get; private set; }

    /// <summary>Número de resolución de contribuyente especial, si aplica.</summary>
    public string? SpecialTaxpayerResolution { get; private set; }

    public bool IsWithholdingAgent { get; private set; }

    /// <summary>Código de régimen tributario. Catálogo pendiente de verificación oficial.</summary>
    public string? TaxRegimeCode { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public static Taxpayer Create(
        Guid id,
        Guid tenantId,
        string ruc,
        string legalName,
        string? tradeName,
        bool keepsAccounting,
        string? specialTaxpayerResolution,
        bool isWithholdingAgent,
        string? taxRegimeCode,
        DateOnly validFrom)
    {
        var validation = EcuadorIdValidator.ValidateRuc(ruc);
        if (!validation.IsValid)
        {
            throw new DomainException("ruc_invalid", string.Join(" ", validation.Errors));
        }

        return new Taxpayer
        {
            Id = id,
            TenantId = tenantId,
            Ruc = ruc,
            LegalName = legalName.Trim(),
            TradeName = tradeName?.Trim(),
            Kind = validation.Kind,
            KeepsAccounting = keepsAccounting,
            SpecialTaxpayerResolution = specialTaxpayerResolution,
            IsWithholdingAgent = isWithholdingAgent,
            TaxRegimeCode = taxRegimeCode,
            ValidFrom = validFrom,
        };
    }
}

/// <summary>Establecimiento SRI (código de 3 dígitos) de un contribuyente. Puede asociarse a una sucursal física.</summary>
public sealed class Establishment : ITenantOwned
{
    private Establishment()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid TaxpayerId { get; private set; }

    public Guid? BranchId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Address { get; private set; }

    public bool IsActive { get; private set; }

    public static Establishment Create(Guid id, Guid tenantId, Guid taxpayerId, Guid? branchId, string code, string name, string? address)
    {
        EnsureSriCode(code, "establecimiento");
        return new Establishment
        {
            Id = id,
            TenantId = tenantId,
            TaxpayerId = taxpayerId,
            BranchId = branchId,
            Code = code,
            Name = name.Trim(),
            Address = address?.Trim(),
            IsActive = true,
        };
    }

    internal static void EnsureSriCode(string code, string label)
    {
        if (code.Length != 3 || !code.All(char.IsAsciiDigit) || code == "000")
        {
            throw new DomainException("sri_code_invalid", $"El código de {label} debe tener 3 dígitos y ser distinto de 000.");
        }
    }
}

/// <summary>Punto de emisión SRI (código de 3 dígitos) de un establecimiento. Los secuenciales por tipo de comprobante pertenecen a Ventas/SRI.</summary>
public sealed class EmissionPoint : ITenantOwned
{
    private EmissionPoint()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid EstablishmentId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public static EmissionPoint Create(Guid id, Guid tenantId, Guid establishmentId, string code, string description)
    {
        Establishment.EnsureSriCode(code, "punto de emisión");
        return new EmissionPoint
        {
            Id = id,
            TenantId = tenantId,
            EstablishmentId = establishmentId,
            Code = code,
            Description = description.Trim(),
            IsActive = true,
        };
    }
}
