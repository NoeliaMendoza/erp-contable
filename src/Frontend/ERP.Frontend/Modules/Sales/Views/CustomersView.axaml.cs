using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ERP.Frontend.Modules.Sales.Views;

public partial class CustomersView : UserControl
{
    public CustomersView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
