using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Modules.Inventory.ViewModels;

/// <summary>Categorías del catálogo: listado paginado, búsqueda, alta, edición y activar/desactivar.</summary>
public sealed partial class CategoriesViewModel : ViewModelBase
{
    private const int PageSize = 20;
    private readonly InventoryApi _api;

    public CategoriesViewModel(InventoryApi api, SessionState session)
    {
        _api = api;
        CanWrite = session.Can("inventory.categories.write");
        _ = LoadCommand.ExecuteAsync(null);
    }

    /// <summary>Solo UX: la API rechaza la escritura si no hay permiso.</summary>
    public bool CanWrite { get; }

    public ObservableCollection<CategoryItem> Categories { get; } = [];

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
    public partial CategoryItem? Editing { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    [ObservableProperty]
    public partial CategoryItem? PendingToggle { get; set; }

    public string FormTitle => Editing is null ? "Nueva categoría" : $"Editar {Editing.Name}";

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var result = await _api.ListCategoriesAsync(Page, PageSize, Search, IncludeInactive ? null : true);
        Categories.Clear();
        foreach (var category in result.Items)
        {
            Categories.Add(category);
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
    private void Edit(CategoryItem category)
    {
        Editing = category;
        Name = category.Name;
        ClearMessages();
    }

    [RelayCommand]
    private void NewCategory()
    {
        Editing = null;
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
                    ? await _api.CreateCategoryAsync(new SaveCategory(Name))
                    : await _api.UpdateCategoryAsync(Editing.Id, new SaveCategory(Name));
                NewCategory();
                SuccessMessage = $"Categoría {saved.Name} guardada.";
            }
            catch (ApiException ex) when (ex.Problem?.Errors is { Count: > 0 } errors)
            {
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
    private void AskToggle(CategoryItem category) => PendingToggle = category;

    [RelayCommand]
    private void CancelToggle() => PendingToggle = null;

    [RelayCommand]
    private async Task ConfirmToggleAsync()
    {
        if (PendingToggle is not { } category)
        {
            return;
        }

        PendingToggle = null;
        ClearMessages();
        await RunAsync(async () =>
        {
            var updated = await _api.SetCategoryStatusAsync(category.Id, !category.IsActive);
            SuccessMessage = $"Categoría {updated.Name} {(updated.IsActive ? "activada" : "desactivada")}.";
        });
        await LoadAsync();
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        NameError = null;
    }
}
