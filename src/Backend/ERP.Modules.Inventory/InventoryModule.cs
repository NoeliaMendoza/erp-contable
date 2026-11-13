using ERP.BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Modules.Inventory;

/// <summary>
/// Módulo Inventario (Integrante 5). Estado: <b>pendiente</b>. Implementará los contratos de
/// <c>ERP.Modules.Inventory.Contracts</c> (catálogo, existencias y movimientos).
/// </summary>
public static class InventoryModule
{
    public const string Module = "inventory";

    public static readonly ModuleDescriptor Descriptor = new(Module, "Inventario", "Integrante 5");

    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        services.AddControllers().AddApplicationPart(typeof(InventoryModule).Assembly);
        services.AddSingleton<IPermissionProvider, InventoryPermissionProvider>();
        return services;
    }
}

public static class InventoryPermissions
{
    public const string ProductsRead = "inventory.products.read";
    public const string ProductsManage = "inventory.products.manage";
    public const string WarehousesManage = "inventory.warehouses.manage";
    public const string MovementsRegister = "inventory.movements.register";
    public const string KardexRead = "inventory.kardex.read";
}

public sealed class InventoryPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(InventoryPermissions.ProductsRead, InventoryModule.Module, "Consultar productos y servicios"),
        new(InventoryPermissions.ProductsManage, InventoryModule.Module, "Administrar productos y servicios"),
        new(InventoryPermissions.WarehousesManage, InventoryModule.Module, "Administrar bodegas"),
        new(InventoryPermissions.MovementsRegister, InventoryModule.Module, "Registrar movimientos de inventario"),
        new(InventoryPermissions.KardexRead, InventoryModule.Module, "Consultar kardex y existencias"),
    ];
}
