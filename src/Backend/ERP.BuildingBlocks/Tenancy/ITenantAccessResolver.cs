namespace ERP.BuildingBlocks.Tenancy;

/// <summary>Resultado de validar que un usuario puede operar en un tenant.</summary>
public sealed record TenantAccessResult(bool Granted, IReadOnlyCollection<string> Permissions)
{
    public static TenantAccessResult Denied { get; } = new(false, []);
}

/// <summary>
/// Valida la membresía activa de un usuario en un tenant y devuelve sus permisos efectivos.
/// Implementado por el módulo Platform; consumido por la API al resolver el tenant de cada solicitud.
/// </summary>
public interface ITenantAccessResolver
{
    Task<TenantAccessResult> ResolveAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>Persona activa del directorio global (datos que viajan como claims del token).</summary>
public sealed record DirectoryUser(Guid Id, string Email, string DisplayName);

/// <summary>Directorio global de usuarios (identidad), independiente del tenant.</summary>
public interface IUserDirectory
{
    Task<Guid?> FindActiveUserIdByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Devuelve la persona si existe y está activa; <c>null</c> en otro caso (inicio de sesión y renovación de token).</summary>
    Task<DirectoryUser?> FindActiveUserAsync(Guid userId, CancellationToken cancellationToken);
}
