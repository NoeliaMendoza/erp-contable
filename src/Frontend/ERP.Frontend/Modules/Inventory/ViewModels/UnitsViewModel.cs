using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Modules.Inventory.ViewModels;

/// <summary>Mantenimiento de unidades de medida: listado paginado, búsqueda, alta, edición y activar/desactivar.</summary>
public sealed partial class UnitsViewModel : ViewModelBase
{
    private const int PageSize = 20;
    private readonly InventoryApi _api;

    public UnitsViewModel(InventoryApi api, SessionState session)
    {
        _api = api;
        CanWrite = session.Can("inventory.units.write");
        _ = LoadCommand.ExecuteAsync(null);
    }

    /// <summary>Solo UX: la API rechaza la escritura si no hay permiso.</summary>
    public bool CanWrite { get; }

    public ObservableCollection<UnitItem> Units { get; } = [];

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial bool IncludeInactive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    public partial int Page { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    public partial int TotalPages { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial int TotalCount { get; set; }

    public string PageLabel => $"Página {Page} de {Math.Max(TotalPages, 1)}";

    public bool IsEmpty => TotalCount == 0 && !IsBusy;

    // Formulario
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    public partial UnitItem? Editing { get; set; }

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? CodeError { get; set; }

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    [ObservableProperty]
    public partial UnitItem? PendingToggle { get; set; }

    public string FormTitle => Editing is null ? "Nueva unidad" : $"Editar {Editing.Code}";

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var result = await _api.ListUnitsAsync(Page, PageSize, Search, IncludeInactive ? null : true);
        Units.Clear();
        foreach (var unit in result.Items)
        {
            Units.Add(unit);
        }

        TotalCount = result.TotalCount;
        TotalPages = result.TotalPages;
        OnPropertyChanged(nameof(IsEmpty));
    });

    [RelayCommand]
    private Task SearchNowAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    [RelayCommand]
    private Task NextPageAsync()
    {
        if (Page >= TotalPages)
        {
            return Task.CompletedTask;
        }

        Page++;
        return LoadAsync();
    }

    [RelayCommand]
    private Task PreviousPageAsync()
    {
        if (Page <= 1)
        {
            return Task.CompletedTask;
        }

        Page--;
        return LoadAsync();
    }

    [RelayCommand]
    private void Edit(UnitItem unit)
    {
        Editing = unit;
        Code = unit.Code;
        Name = unit.Name;
        ClearMessages();
    }

    [RelayCommand]
    private void NewUnit()
    {
        Editing = null;
        Code = string.Empty;
        Name = string.Empty;
        ClearMessages();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ClearMessages();
        await RunAsync(async () =>
        {
            try
            {
                var saved = Editing is null
                    ? await _api.CreateUnitAsync(new SaveUnit(Code, Name))
                    : await _api.UpdateUnitAsync(Editing.Id, new SaveUnit(Code, Name));
                NewUnit();
                SuccessMessage = $"Unidad {saved.Code} guardada.";
            }
            catch (ApiException ex) when (ex.Problem?.Errors is { Count: > 0 } errors)
            {
                CodeError = FieldError.For(errors, "Code");
                NameError = FieldError.For(errors, "Name");
                throw;
            }
        });

        if (ErrorMessage is null)
        {
            await LoadAsync();
        }
    }

    /// <summary>Primer clic: pide confirmación. La confirmación ejecuta el cambio.</summary>
    [RelayCommand]
    private void AskToggle(UnitItem unit) => PendingToggle = unit;

    [RelayCommand]
    private void CancelToggle() => PendingToggle = null;

    [RelayCommand]
    private async Task ConfirmToggleAsync()
    {
        if (PendingToggle is not { } unit)
        {
            return;
        }

        PendingToggle = null;
        ClearMessages();
        await RunAsync(async () =>
        {
            var updated = await _api.SetUnitStatusAsync(unit.Id, !unit.IsActive);
            SuccessMessage = $"Unidad {updated.Code} {(updated.IsActive ? "activada" : "desactivada")}.";
        });
        await LoadAsync();
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        CodeError = null;
        NameError = null;
    }
}

internal static class FieldError
{
    /// <summary>Busca el error de un campo en ProblemDetails (las claves pueden venir con distinta capitalización).</summary>
    public static string? For(Dictionary<string, string[]> errors, string field) =>
        errors.FirstOrDefault(e => string.Equals(e.Key, field, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
}
