namespace ERP.Modules.Sales.Dtos;

public sealed record CustomerResponse(
    Guid Id,
    string IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    bool IsActive,
    string? IdentificationWarning);

public sealed record SaveCustomerRequest(
    string IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Email,
    string? Phone,
    string? Address);

public sealed record ChangeCustomerStatusRequest(bool IsActive);
