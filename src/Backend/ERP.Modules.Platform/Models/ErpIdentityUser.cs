using Microsoft.AspNetCore.Identity;

namespace ERP.Modules.Platform.Models;

/// <summary>
/// Credenciales de ASP.NET Core Identity (hash de contraseña, bloqueo, sello de seguridad) de una persona.
/// Usa el <b>mismo Id</b> que <see cref="AppUser"/> (tabla <c>platform.users</c>), que sigue siendo el directorio
/// de usuarios y la fuente del claim <c>sub</c>. Las membresías y permisos por empresa NO viven aquí.
/// </summary>
public sealed class ErpIdentityUser : IdentityUser<Guid>
{
    public ErpIdentityUser()
    {
    }

    public ErpIdentityUser(Guid id, string email)
    {
        var normalized = AppUser.NormalizeEmail(email);
        Id = id;
        UserName = normalized;
        Email = normalized;
        EmailConfirmed = true;
        LockoutEnabled = true;
        SecurityStamp = Guid.NewGuid().ToString("N");
    }
}
