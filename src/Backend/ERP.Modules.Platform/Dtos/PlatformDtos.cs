namespace ERP.Modules.Platform.Dtos;

// DTOs públicos v1. Nunca se devuelven entidades EF. Cambios incompatibles requieren /api/v2.

public sealed record MyProfileResponse(Guid UserId, string Email, string DisplayName, IReadOnlyList<MembershipResponse> Memberships);

public sealed record MembershipResponse(Guid TenantId, string TenantName);

public sealed record CurrentTenantResponse(
    Guid TenantId,
    string Name,
    string Status,
    IReadOnlyList<string> EnabledFeatures,
    IReadOnlyList<string> MyPermissions);

public sealed record BranchResponse(Guid Id, string Code, string Name, string? Address, string? City, bool IsActive);

public sealed record CreateBranchRequest(string Code, string Name, string? Address, string? City);

public sealed record TaxpayerResponse(
    Guid Id,
    string Ruc,
    string LegalName,
    string? TradeName,
    string Kind,
    bool KeepsAccounting,
    bool IsWithholdingAgent,
    string? SpecialTaxpayerResolution,
    DateOnly ValidFrom,
    IReadOnlyList<EstablishmentResponse> Establishments);

public sealed record EstablishmentResponse(Guid Id, string Code, string Name, string? Address, Guid? BranchId, IReadOnlyList<EmissionPointResponse> EmissionPoints);

public sealed record EmissionPointResponse(Guid Id, string Code, string Description, bool IsActive);
