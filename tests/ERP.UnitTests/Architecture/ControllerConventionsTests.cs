using System.Reflection;
using ERP.Infrastructure.Web;
using ERP.Modules.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.UnitTests.Architecture;

/// <summary>Convenciones MVC: cada controlador vive en Controllers/, hereda de ApiControllerBase y protege sus acciones.</summary>
public sealed class ControllerConventionsTests
{
    /// <summary>Acciones que no requieren empresa activa (solo autenticación). Mantener la lista corta y revisada.</summary>
    private static readonly HashSet<string> TenantFreeActions = ["MeController.Get"];

    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(PlatformModule).Assembly,
        typeof(ERP.Modules.Accounting.AccountingModule).Assembly,
    ];

    private static IEnumerable<Type> Controllers() => ModuleAssemblies
        .SelectMany(a => a.GetTypes())
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    [Fact]
    public void Existen_controladores_en_Platform()
    {
        Assert.NotEmpty(Controllers());
    }

    [Fact]
    public void Controladores_heredan_de_ApiControllerBase_y_estan_en_Controllers()
    {
        Assert.All(Controllers(), c =>
        {
            Assert.True(typeof(ApiControllerBase).IsAssignableFrom(c), $"{c.Name} debe heredar de ApiControllerBase");
            Assert.EndsWith(".Controllers", c.Namespace, StringComparison.Ordinal);
            Assert.EndsWith("Controller", c.Name, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Toda_accion_exige_permiso_por_empresa_salvo_excepciones_explicitas()
    {
        var actions = Controllers().SelectMany(c => c
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(m => (Controller: c, Method: m)));

        Assert.All(actions, a =>
        {
            var name = $"{a.Controller.Name}.{a.Method.Name}";
            var hasPermission = a.Method.GetCustomAttributes<RequirePermissionAttribute>().Any()
                || a.Controller.GetCustomAttributes<RequirePermissionAttribute>().Any();

            Assert.True(hasPermission || TenantFreeActions.Contains(name), $"{name} no declara [RequirePermission]");
            Assert.Empty(a.Method.GetCustomAttributes<AllowAnonymousAttribute>());
        });
    }
}
