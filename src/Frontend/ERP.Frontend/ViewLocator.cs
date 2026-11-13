using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ERP.Frontend.Modules.Platform.ViewModels;
using ERP.Frontend.Modules.Platform.Views;
using ERP.Frontend.Shared.Mvvm;
using ERP.Frontend.Shell;

namespace ERP.Frontend;

/// <summary>
/// Asocia ViewModels con vistas de forma explícita (sin reflexión, compatible con trimming).
/// Cada módulo agrega aquí sus pares VM → Vista.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Views = new()
    {
        [typeof(LoginViewModel)] = () => new LoginView(),
        [typeof(TenantSelectionViewModel)] = () => new TenantSelectionView(),
        [typeof(ShellViewModel)] = () => new ShellView(),
        [typeof(DashboardViewModel)] = () => new DashboardView(),
        [typeof(BranchesViewModel)] = () => new BranchesView(),
        [typeof(ModulePlaceholderViewModel)] = () => new ModulePlaceholderView(),
    };

    public Control Build(object? param) =>
        param is not null && Views.TryGetValue(param.GetType(), out var factory)
            ? factory()
            : new TextBlock { Text = $"Vista no registrada para {param?.GetType().Name}" };

    public bool Match(object? data) => data is ViewModelBase;
}
