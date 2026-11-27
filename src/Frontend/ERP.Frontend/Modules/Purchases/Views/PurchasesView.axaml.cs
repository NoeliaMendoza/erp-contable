using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using ERP.Frontend.Modules.Purchases.ViewModels;
using ERP.Frontend.Shared.Controls;

namespace ERP.Frontend.Modules.Purchases.Views;

public partial class PurchasesView : UserControl
{
    public PurchasesView() => InitializeComponent();

    private void ShowReceptionConfirmation(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PurchaseItem purchase } button || DataContext is not PurchasesViewModel model)
            return;

        model.AskConfirmationCommand.Execute(purchase);
        if (!ReferenceEquals(model.PendingConfirmation, purchase))
            return;

        var flyout = new Flyout { Placement = PlacementMode.TopEdgeAlignedRight };
        flyout.Content = new ConfirmationDialog
        {
            Title = "Confirmar recepción",
            Message = $"¿Confirmar la compra {purchase.Number} y registrar su entrada de inventario y asiento contable?",
            ConfirmCommand = new AsyncRelayCommand(async () =>
            {
                var confirmation = model.ConfirmCommand.ExecuteAsync(null);
                flyout.Hide();
                await confirmation;
            }),
            CancelCommand = new RelayCommand(flyout.Hide),
            Width = 420,
        };
        flyout.Closed += (_, _) => model.CancelConfirmationCommand.Execute(null);
        flyout.ShowAt(button);
    }
}
