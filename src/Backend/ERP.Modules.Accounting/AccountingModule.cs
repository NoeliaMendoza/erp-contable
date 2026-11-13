using ERP.BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Modules.Accounting;

/// <summary>
/// Módulo Contabilidad (Integrante 2). Estado: dominio de asientos con partida doble <b>implementado y probado</b>;
/// persistencia, endpoints e <c>IAccountingPostingService</c> <b>pendientes</b>.
/// </summary>
public static class AccountingModule
{
    public const string Module = "accounting";

    public static readonly ModuleDescriptor Descriptor = new(Module, "Contabilidad", "Integrante 2");

    public static IServiceCollection AddAccountingModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        services.AddControllers().AddApplicationPart(typeof(AccountingModule).Assembly);
        services.AddSingleton<IPermissionProvider, AccountingPermissionProvider>();
        return services;
    }
}

public static class AccountingPermissions
{
    public const string AccountsRead = "accounting.accounts.read";
    public const string AccountsManage = "accounting.accounts.manage";
    public const string EntriesRead = "accounting.entries.read";
    public const string EntriesPost = "accounting.entries.post";
    public const string EntriesReverse = "accounting.entries.reverse";
    public const string PeriodsClose = "accounting.periods.close";
}

public sealed class AccountingPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(AccountingPermissions.AccountsRead, AccountingModule.Module, "Consultar plan de cuentas"),
        new(AccountingPermissions.AccountsManage, AccountingModule.Module, "Administrar plan de cuentas"),
        new(AccountingPermissions.EntriesRead, AccountingModule.Module, "Consultar asientos y libros"),
        new(AccountingPermissions.EntriesPost, AccountingModule.Module, "Contabilizar asientos"),
        new(AccountingPermissions.EntriesReverse, AccountingModule.Module, "Reversar asientos contabilizados"),
        new(AccountingPermissions.PeriodsClose, AccountingModule.Module, "Cerrar y reabrir períodos contables"),
    ];
}
