using Avalonia;
using Avalonia.Controls;

namespace ERP.Frontend.Shared.Controls;

/// <summary>Logo de Orvix. <see cref="ShowWordmark"/> en false muestra solo el monograma (ícono).</summary>
public partial class OrvixLogo : UserControl
{
    public static readonly StyledProperty<bool> ShowWordmarkProperty =
        AvaloniaProperty.Register<OrvixLogo, bool>(nameof(ShowWordmark), defaultValue: true);

    public OrvixLogo()
    {
        InitializeComponent();
    }

    public bool ShowWordmark
    {
        get => GetValue(ShowWordmarkProperty);
        set => SetValue(ShowWordmarkProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowWordmarkProperty)
        {
            Wordmark.IsVisible = change.GetNewValue<bool>();
        }
    }
}
