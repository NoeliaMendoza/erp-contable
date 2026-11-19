using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace ERP.Frontend.Shared.Controls;

/// <summary>Estado vacío que explica al usuario qué puede hacer a continuación.</summary>
public partial class EmptyState : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(Title), "Sin registros");

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(Message), "Todavía no hay datos para mostrar.");

    public static readonly StyledProperty<string> ActionTextProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(ActionText), "Crear");

    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<EmptyState, ICommand?>(nameof(ActionCommand));

    public EmptyState() => InitializeComponent();

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }
}
