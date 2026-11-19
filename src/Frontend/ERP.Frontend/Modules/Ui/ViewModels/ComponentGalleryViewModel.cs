using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Mvvm;

namespace ERP.Frontend.Modules.Ui.ViewModels;

/// <summary>Galería de desarrollo para revisar los componentes visuales compartidos.</summary>
public sealed partial class ComponentGalleryViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string SampleName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "sucursal";

    [ObservableProperty]
    public partial string? SampleNameError { get; set; }

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial int CurrentPage { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial int TotalPages { get; set; } = 3;

    [ObservableProperty]
    public partial bool IsConfirmed { get; set; }

    public bool CanGoPrevious => CurrentPage > 1;

    public bool CanGoNext => CurrentPage < TotalPages;

    [RelayCommand]
    private void Save()
    {
        SampleNameError = string.IsNullOrWhiteSpace(SampleName)
            ? "Escriba un nombre para comprobar la validación de campo."
            : null;

        SuccessMessage = null;
        if (SampleNameError is null)
        {
            SuccessMessage = $"El ejemplo «{SampleName.Trim()}» está listo para conectarse a un CRUD.";
        }
    }

    [RelayCommand]
    private async Task SimulateLoadAsync()
    {
        SuccessMessage = null;
        await RunAsync(async () =>
        {
            await Task.Delay(250);
            SuccessMessage = "Carga completada. El mismo estado se usa en las pantallas reales.";
        });
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPage() => CurrentPage--;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage() => CurrentPage++;

    [RelayCommand]
    private void Confirm()
    {
        IsConfirmed = true;
        SuccessMessage = "Acción confirmada. En un CRUD, aquí se ejecutaría el comando del módulo.";
    }

    [RelayCommand]
    private void Cancel()
    {
        IsConfirmed = false;
        SuccessMessage = "Acción cancelada sin modificar datos.";
    }

    [RelayCommand]
    private void Reset()
    {
        SampleName = string.Empty;
        SampleNameError = null;
        SuccessMessage = null;
        IsConfirmed = false;
    }
}
