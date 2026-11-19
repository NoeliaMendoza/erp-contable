using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace ERP.Frontend.Shared.Controls;

/// <summary>Barra de paginación sin lógica de negocio ni acceso a datos.</summary>
public partial class PaginationBar : UserControl
{
    public static readonly StyledProperty<int> CurrentPageProperty =
        AvaloniaProperty.Register<PaginationBar, int>(nameof(CurrentPage), 1);

    public static readonly StyledProperty<int> TotalPagesProperty =
        AvaloniaProperty.Register<PaginationBar, int>(nameof(TotalPages), 1);

    public static readonly StyledProperty<bool> CanGoPreviousProperty =
        AvaloniaProperty.Register<PaginationBar, bool>(nameof(CanGoPrevious));

    public static readonly StyledProperty<bool> CanGoNextProperty =
        AvaloniaProperty.Register<PaginationBar, bool>(nameof(CanGoNext));

    public static readonly StyledProperty<ICommand?> PreviousCommandProperty =
        AvaloniaProperty.Register<PaginationBar, ICommand?>(nameof(PreviousCommand));

    public static readonly StyledProperty<ICommand?> NextCommandProperty =
        AvaloniaProperty.Register<PaginationBar, ICommand?>(nameof(NextCommand));

    public PaginationBar() => InitializeComponent();

    public int CurrentPage
    {
        get => GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    public int TotalPages
    {
        get => GetValue(TotalPagesProperty);
        set => SetValue(TotalPagesProperty, value);
    }

    public bool CanGoPrevious
    {
        get => GetValue(CanGoPreviousProperty);
        set => SetValue(CanGoPreviousProperty, value);
    }

    public bool CanGoNext
    {
        get => GetValue(CanGoNextProperty);
        set => SetValue(CanGoNextProperty, value);
    }

    public ICommand? PreviousCommand
    {
        get => GetValue(PreviousCommandProperty);
        set => SetValue(PreviousCommandProperty, value);
    }

    public ICommand? NextCommand
    {
        get => GetValue(NextCommandProperty);
        set => SetValue(NextCommandProperty, value);
    }
}
