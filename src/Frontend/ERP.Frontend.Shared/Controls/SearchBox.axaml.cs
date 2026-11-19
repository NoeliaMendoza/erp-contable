using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace ERP.Frontend.Shared.Controls;

/// <summary>Campo de búsqueda con una acción de limpieza consistente.</summary>
public partial class SearchBox : UserControl
{
    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<SearchBox, string>(nameof(SearchText), string.Empty);

    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<SearchBox, string>(nameof(PlaceholderText), "Buscar");

    public static readonly StyledProperty<ICommand?> ClearCommandProperty =
        AvaloniaProperty.Register<SearchBox, ICommand?>(nameof(ClearCommand));

    public SearchBox() => InitializeComponent();

    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public string PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public ICommand? ClearCommand
    {
        get => GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }
}
