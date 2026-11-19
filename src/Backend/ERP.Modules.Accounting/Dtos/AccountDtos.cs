namespace ERP.Modules.Accounting.Dtos;

// El servidor obtiene la empresa y el actor de la sesión, nunca del cuerpo HTTP.
public sealed record AccountResponse(Guid Id, string Code, string Name, string Kind, string NormalBalance, Guid? ParentId, bool AllowsPosting, bool IsActive);

public sealed record SaveAccountRequest(string Code, string Name, string Kind, string NormalBalance, Guid? ParentId, bool AllowsPosting);

/// <summary>Nullable para distinguir un estado explícito de un cuerpo vacío y evitar bajas accidentales.</summary>
public sealed record ChangeAccountStatusRequest(bool? IsActive);
