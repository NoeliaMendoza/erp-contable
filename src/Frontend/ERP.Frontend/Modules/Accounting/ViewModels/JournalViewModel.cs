using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Shared.Mvvm;

namespace ERP.Frontend.Modules.Accounting.ViewModels;

/// <summary>
/// Libro diario de la empresa activa. Solo consulta: los asientos no se editan ni se borran,
/// de modo que la pantalla no ofrece acciones de escritura. El filtro usa la fecha contable.
/// </summary>
public sealed partial class JournalViewModel : ViewModelBase
{
    private const int PageSize = 20;
    private readonly JournalApi _api;

    public JournalViewModel(JournalApi api)
    {
        _api = api;
        _ = LoadCommand.ExecuteAsync(null);
    }

    public ObservableCollection<JournalEntryItem> Entries { get; } = [];

    [ObservableProperty]
    public partial DateTime? From { get; set; }

    [ObservableProperty]
    public partial DateTime? To { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial int Page { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial int TotalPages { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial string? FilterError { get; set; }

    public string PageLabel => $"Página {Page} de {Math.Max(TotalPages, 1)} · {TotalCount} asiento(s)";

    public bool CanGoPrevious => !IsBusy && Page > 1;

    public bool CanGoNext => !IsBusy && Page < TotalPages;

    public bool IsEmpty => TotalCount == 0 && !IsBusy && ErrorMessage is null;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var from = AsDate(From);
        var to = AsDate(To);
        if (from is { } start && to is { } end && start > end)
        {
            FilterError = "La fecha inicial no puede ser posterior a la final.";
            return;
        }

        FilterError = null;
        await RunAsync(async () =>
        {
            var result = await _api.ListAsync(Page, PageSize, from, to);
            if (result.Items.Count == 0 && Page > 1)
            {
                Page = Math.Max(1, result.TotalPages);
                result = await _api.ListAsync(Page, PageSize, from, to);
            }

            Entries.Clear();
            foreach (var entry in result.Items)
            {
                Entries.Add(entry);
            }

            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
        });

        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>Un cambio de filtro vuelve a la primera página para no mostrar una página vacía.</summary>
    [RelayCommand]
    private Task FilterAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    [RelayCommand]
    private Task ClearFiltersAsync()
    {
        From = null;
        To = null;
        Page = 1;
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
    private Task NextPageAsync()
    {
        if (Page >= TotalPages)
        {
            return Task.CompletedTask;
        }

        Page++;
        return LoadAsync();
    }

    private static DateOnly? AsDate(DateTime? value) => value is { } date ? DateOnly.FromDateTime(date) : null;
}
