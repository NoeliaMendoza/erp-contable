using ERP.BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Modules.Reporting;

/// <summary>
/// Módulo Reportes y experiencia (Integrante 6). Estado: <b>pendiente</b>. Consume consultas de lectura
/// expuestas por los demás módulos; no accede a sus tablas privadas. QuestPDF/ClosedXML se añadirán
/// tras revisar licencias (docs/decisiones-pendientes.md).
/// </summary>
public static class ReportingModule
{
    public const string Module = "reporting";

    public static readonly ModuleDescriptor Descriptor = new(Module, "Reportes", "Integrante 6");

    public static IServiceCollection AddReportingModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        services.AddControllers().AddApplicationPart(typeof(ReportingModule).Assembly);
        services.AddSingleton<IPermissionProvider, ReportingPermissionProvider>();
        return services;
    }
}

public static class ReportingPermissions
{
    public const string DashboardRead = "reporting.dashboard.read";
    public const string ReportsExport = "reporting.reports.export";
    public const string TaxReconciliationRead = "reporting.tax.read";
}

public sealed class ReportingPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(ReportingPermissions.DashboardRead, ReportingModule.Module, "Ver dashboard"),
        new(ReportingPermissions.ReportsExport, ReportingModule.Module, "Exportar reportes PDF/Excel"),
        new(ReportingPermissions.TaxReconciliationRead, ReportingModule.Module, "Ver conciliaciones tributarias internas"),
    ];
}
