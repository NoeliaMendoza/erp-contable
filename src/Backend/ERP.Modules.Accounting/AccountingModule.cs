using ERP.BuildingBlocks.Security;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Accounting.Data;
using ERP.Modules.Accounting.Services;
using ERP.Modules.Accounting.Validators;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Modules.Accounting;

/// <summary>
/// Módulo Contabilidad (Romero). ACC-01: CRUD del plan de cuentas con persistencia y aislamiento por empresa.
/// El dominio de asientos existente se conserva; su persistencia e IAccountingPostingService siguen pendientes.
/// </summary>
public static class AccountingModule
{
    public const string Module = "accounting";

    public static readonly ModuleDescriptor Descriptor = new(Module, "Contabilidad", "Integrante 2");

    public static IServiceCollection AddAccountingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers().AddApplicationPart(typeof(AccountingModule).Assembly);
        services.AddModuleDbContext<AccountingDbContext>(configuration, AccountingDbContext.Schema);
        services.AddScoped<AccountService>();
        services.AddValidatorsFromAssemblyContaining<SaveAccountRequestValidator>();
        services.AddSingleton<IPermissionProvider, AccountingPermissionProvider>();
        return services;
    }
}

public static class AccountingPermissions
{
    public const string AccountsRead = "accounting.accounts.read";
    public const string AccountsWrite = "accounting.accounts.write";

    // Compatibilidad con el catálogo anterior; los endpoints ACC-01 solo autorizan AccountsWrite.
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
        new(AccountingPermissions.AccountsWrite, AccountingModule.Module, "Crear, editar y desactivar cuentas contables"),
        new(AccountingPermissions.AccountsManage, AccountingModule.Module, "Administrar plan de cuentas"),
        new(AccountingPermissions.EntriesRead, AccountingModule.Module, "Consultar asientos y libros"),
        new(AccountingPermissions.EntriesPost, AccountingModule.Module, "Contabilizar asientos"),
        new(AccountingPermissions.EntriesReverse, AccountingModule.Module, "Reversar asientos contabilizados"),
        new(AccountingPermissions.PeriodsClose, AccountingModule.Module, "Cerrar y reabrir períodos contables"),
    ];
}
