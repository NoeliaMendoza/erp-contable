using System.Diagnostics.CodeAnalysis;

namespace ERP.Modules.Accounting.Dtos;

// El servidor obtiene la empresa y el actor de la sesión, nunca del cuerpo HTTP.
public sealed record AccountResponse(Guid Id, string Code, string Name, string Kind, string NormalBalance, Guid? ParentId, bool AllowsPosting, bool IsActive);

/// <summary>
/// Cuerpo de creación y edición de cuentas. Los datos obligatorios se declaran con <c>required</c>
/// (S6964) para que el enlace de modelos rechace un cuerpo incompleto en lugar de completarlo
/// con cadenas vacías o <c>false</c>. <c>ParentId</c> sigue siendo opcional: su ausencia es una cuenta raíz.
/// </summary>
public sealed record SaveAccountRequest
{
    [SetsRequiredMembers]
    public SaveAccountRequest(string code, string name, string kind, string normalBalance, Guid? parentId, bool allowsPosting)
    {
        Code = code;
        Name = name;
        Kind = kind;
        NormalBalance = normalBalance;
        ParentId = parentId;
        AllowsPosting = allowsPosting;
    }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Kind { get; init; }

    public required string NormalBalance { get; init; }

    public Guid? ParentId { get; init; }

    public required bool AllowsPosting { get; init; }
}

/// <summary>Nullable para distinguir un estado explícito de un cuerpo vacío y evitar bajas accidentales.</summary>
public sealed record ChangeAccountStatusRequest(bool? IsActive);
