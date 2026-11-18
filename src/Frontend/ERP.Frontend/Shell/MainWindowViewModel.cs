using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Shell;

/// <summary>Raíz de navegación: login → selección de empresa → shell.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase, INavigator
{
    public MainWindowViewModel(PageFactory pages, IAuthenticationService auth, SessionState session)
    {
        pages.Navigator = this;
        CurrentPage = pages.Login();

        // Expiración (token vencido sin renovación posible o 401 de la API): una sola vez, vuelve al inicio de sesión.
        auth.SessionEnded += (_, e) =>
        {
            if (e.Reason != SessionEndReason.Expired)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                session.SignOut();
                NavigateTo(pages.Login("Su sesión expiró. Inicie sesión nuevamente."));
            });
        };
    }

    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    public void NavigateTo(ViewModelBase page) => CurrentPage = page;
}
