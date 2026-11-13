using CommunityToolkit.Mvvm.ComponentModel;
using ERP.Frontend.Shared.Mvvm;

namespace ERP.Frontend.Shell;

/// <summary>Raíz de navegación: login → selección de empresa → shell.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase, INavigator
{
    public MainWindowViewModel(PageFactory pages)
    {
        pages.Navigator = this;
        CurrentPage = pages.Login();
    }

    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    public void NavigateTo(ViewModelBase page) => CurrentPage = page;
}
