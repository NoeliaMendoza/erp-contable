namespace ERP.Frontend.Shared.Api;

// Modelos del cliente para /api/v1. Se duplican intencionalmente respecto al backend para que el
// escritorio no dependa de ensamblados del servidor; los cambios de contrato se versionan en la API.

public sealed record MyProfile(Guid UserId, string Email, string DisplayName, IReadOnlyList<Membership> Memberships);

public sealed record Membership(Guid TenantId, string TenantName);

public sealed record CurrentTenant(Guid TenantId, string Name, string Status, IReadOnlyList<string> EnabledFeatures, IReadOnlyList<string> MyPermissions);

public sealed record Branch(Guid Id, string Code, string Name, string? Address, string? City, bool IsActive);

public sealed record CreateBranch(string Code, string Name, string? Address, string? City);

public sealed record Taxpayer(
    Guid Id,
    string Ruc,
    string LegalName,
    string? TradeName,
    string Kind,
    bool KeepsAccounting,
    bool IsWithholdingAgent,
    string? SpecialTaxpayerResolution,
    DateOnly ValidFrom,
    IReadOnlyList<Establishment> Establishments);

public sealed record Establishment(Guid Id, string Code, string Name, string? Address, Guid? BranchId, IReadOnlyList<EmissionPoint> EmissionPoints);

public sealed record EmissionPoint(Guid Id, string Code, string Description, bool IsActive);

/// <summary>Subconjunto de RFC 9457 (ProblemDetails) que devuelve la API.</summary>
public sealed record ProblemDetailsModel(
    string? Title,
    string? Detail,
    int? Status,
    string? Code,
    Dictionary<string, string[]>? Errors);
