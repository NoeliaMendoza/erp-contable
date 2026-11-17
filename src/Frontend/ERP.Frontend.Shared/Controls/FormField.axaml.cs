using Avalonia;
using Avalonia.Controls;

namespace ERP.Frontend.Shared.Controls;

/// <summary>Envuelve un control de formulario con etiqueta, ayuda y error de campo.</summary>
public partial class FormField : UserControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<FormField, string?>(nameof(Label));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<FormField, string?>(nameof(Hint));

    public static readonly StyledProperty<string?> ErrorMessageProperty =
        AvaloniaProperty.Register<FormField, string?>(nameof(ErrorMessage));

    public static readonly StyledProperty<Control?> FieldContentProperty =
        AvaloniaProperty.Register<FormField, Control?>(nameof(FieldContent));

    public FormField() => InitializeComponent();

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public string? ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public Control? FieldContent
    {
        get => GetValue(FieldContentProperty);
        set => SetValue(FieldContentProperty, value);
    }
}
