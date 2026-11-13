using CommunityToolkit.Mvvm.ComponentModel;
using ERP.Frontend.Shared.Api;

namespace ERP.Frontend.Shared.Mvvm;

/// <summary>Base de ViewModels: estado de carga y mensaje de error uniformes.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Ejecuta una operación remota mostrando estado de carga y traduciendo errores de la API.</summary>
    protected async Task RunAsync(Func<Task> operation)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await operation();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (OperationCanceledException)
        {
            // Cancelado por el usuario o por navegación: no es un error.
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Navegación de alto nivel de la ventana principal (login → empresa → shell).</summary>
public interface INavigator
{
    void NavigateTo(ViewModelBase page);
}
