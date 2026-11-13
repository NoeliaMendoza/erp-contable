using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Session;
using ERP.Frontend.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Frontend;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = services.GetRequiredService<MainWindowViewModel>() };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Dirección de la API: variable de entorno ERP_API_BASE_URL o el puerto HTTPS de desarrollo.
        var apiBaseUrl = Environment.GetEnvironmentVariable("ERP_API_BASE_URL") ?? "https://localhost:7180/";
        services.AddSingleton(new FrontendSettings(new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/")));

        services.AddSingleton<SessionState>();
        services.AddTransient<SessionHeadersHandler>();
        services.AddHttpClient<ErpApiClient>((sp, http) =>
            {
                http.BaseAddress = sp.GetRequiredService<FrontendSettings>().ApiBaseUrl;
                http.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<SessionHeadersHandler>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<PageFactory>();

        return services.BuildServiceProvider();
    }
}

public sealed record FrontendSettings(Uri ApiBaseUrl);
