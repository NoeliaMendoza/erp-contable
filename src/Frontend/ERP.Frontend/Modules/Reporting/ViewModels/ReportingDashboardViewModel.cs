using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Mvvm;

namespace ERP.Frontend.Modules.Reporting.ViewModels;

/// <summary>
/// Primera maqueta del resumen de Reporting. Los indicadores se mantienen sin datos
/// hasta conectar las consultas publicadas por Ventas e Inventario.
/// </summary>
public sealed partial class ReportingDashboardViewModel : ViewModelBase
{
    private static DateTimeOffset Today => new(DateTime.Today);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRangeLabel))]
    public partial DateTimeOffset? FromDate { get; set; } = Today.AddDays(-30);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRangeLabel))]
    public partial DateTimeOffset? ToDate { get; set; } = Today;

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    public string SalesTodayLabel => "—";

    public string SalesMonthLabel => "—";

    public string LowStockLabel => "—";

    public string SelectedRangeLabel => FromDate is { } from && ToDate is { } to
        ? $"{from:dd/MM/yyyy} – {to:dd/MM/yyyy}"
        : "Selecciona un periodo";

    [RelayCommand]
    private void ApplyFilters()
    {
        ErrorMessage = null;
        SuccessMessage = "El periodo está listo para consultar los datos del servidor.";
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FromDate = Today.AddDays(-30);
        ToDate = Today;
        ErrorMessage = null;
        SuccessMessage = null;
    }
}
