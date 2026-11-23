using ERP.BuildingBlocks.Security;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Inventory.Contracts;
using ERP.Modules.Inventory.Data;
using ERP.Modules.Inventory.Services;
using ERP.Modules.Inventory.Validators;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Modules.Inventory;

/// <summary>
/// Módulo Inventario (responsable: Mendoza). Estado: catálogo de unidades, categorías y productos/servicios (INV-01) y
/// bodegas, entradas, salidas, existencias y kardex con costo promedio (INV-03) <b>implementados</b>.
/// </summary>
public static class InventoryModule
{
    public const string Module = "inventory";

    public static readonly ModuleDescriptor Descriptor = new(Module, "Inventario", "Mendoza");

    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers().AddApplicationPart(typeof(InventoryModule).Assembly);
        services.AddModuleDbContext<InventoryDbContext>(configuration, InventoryDbContext.Schema);
        services.AddScoped<UnitService>();
        services.AddScoped<ProductService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<StockReportService>();
        services.AddScoped<IProductCatalog, ProductCatalog>();
        services.AddScoped<InventoryStockService>();
        services.AddScoped<IStockQueryService>(sp => sp.GetRequiredService<InventoryStockService>());
        services.AddScoped<IInventoryMovementService>(sp => sp.GetRequiredService<InventoryStockService>());
        services.AddValidatorsFromAssemblyContaining<SaveUnitRequestValidator>();
        services.AddSingleton<IPermissionProvider, InventoryPermissionProvider>();
        return services;
    }
}

public static class InventoryPermissions
{
    public const string UnitsRead = "inventory.units.read";
    public const string UnitsWrite = "inventory.units.write";
    public const string ProductsRead = "inventory.products.read";
    public const string ProductsWrite = "inventory.products.write";
    public const string CategoriesRead = "inventory.categories.read";
    public const string CategoriesWrite = "inventory.categories.write";

    public const string WarehousesManage = "inventory.warehouses.manage";

    /// <summary>Reservado: los movimientos solo los registran Compras y Ventas por contrato, sin endpoint propio.</summary>
    public const string MovementsRegister = "inventory.movements.register";

    /// <summary>Bodegas, existencias y kardex (lectura).</summary>
    public const string KardexRead = "inventory.kardex.read";
}

public sealed class InventoryPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(InventoryPermissions.UnitsRead, InventoryModule.Module, "Consultar unidades de medida"),
        new(InventoryPermissions.UnitsWrite, InventoryModule.Module, "Crear, editar y desactivar unidades de medida"),
        new(InventoryPermissions.ProductsRead, InventoryModule.Module, "Consultar productos y servicios"),
        new(InventoryPermissions.ProductsWrite, InventoryModule.Module, "Crear, editar y desactivar productos y servicios"),
        new(InventoryPermissions.CategoriesRead, InventoryModule.Module, "Consultar categorías del catálogo"),
        new(InventoryPermissions.CategoriesWrite, InventoryModule.Module, "Crear, editar y desactivar categorías del catálogo"),
        new(InventoryPermissions.WarehousesManage, InventoryModule.Module, "Administrar bodegas"),
        new(InventoryPermissions.MovementsRegister, InventoryModule.Module, "Registrar movimientos de inventario"),
        new(InventoryPermissions.KardexRead, InventoryModule.Module, "Consultar kardex y existencias"),
    ];
}
