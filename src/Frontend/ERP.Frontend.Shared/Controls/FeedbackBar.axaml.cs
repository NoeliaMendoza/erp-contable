using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace ERP.Frontend.Shared.Controls;

/// <summary>Presenta carga, error recuperable y confirmación de una operación.</summary>
public partial class FeedbackBar : UserControl
{
    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<FeedbackBar, bool>(nameof(IsBusy));

    public static readonly StyledProperty<string?> ErrorMessageProperty =
        AvaloniaProperty.Register<FeedbackBar, string?>(nameof(ErrorMessage));

    public static readonly StyledProperty<string?> SuccessMessageProperty =
        AvaloniaProperty.Register<FeedbackBar, string?>(nameof(SuccessMessage));

    public static readonly StyledProperty<ICommand?> RetryCommandProperty =
        AvaloniaProperty.Register<FeedbackBar, ICommand?>(nameof(RetryCommand));

    public FeedbackBar() => InitializeComponent();

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string? ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public string? SuccessMessage
    {
        get => GetValue(SuccessMessageProperty);
        set => SetValue(SuccessMessageProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }
}
