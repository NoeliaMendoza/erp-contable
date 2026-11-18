using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Session;
using ERP.Frontend.Shared.Session.Oidc;
using ERP.Frontend.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ERP.Frontend;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // El host ejecuta el servidor local de bucle que recibe la respuesta del navegador (OIDC).
        _host = BuildHost();
        _host.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = _host.Services.GetRequiredService<MainWindowViewModel>() };
            desktop.Exit += (_, _) =>
            {
                _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                _host.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        var services = builder.Services;

        // Dirección de la API (también es el emisor OIDC): ERP_API_BASE_URL o el puerto HTTPS de desarrollo.
        var apiBaseUrl = Environment.GetEnvironmentVariable("ERP_API_BASE_URL") ?? "https://localhost:7180/";
        var settings = new FrontendSettings(new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/"));
        services.AddSingleton(settings);

        // Puerto local donde el navegador devuelve el resultado del inicio de sesión (debe estar registrado en la API).
        var redirectPort = int.TryParse(Environment.GetEnvironmentVariable("ERP_OIDC_REDIRECT_PORT"), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            ? port
            : 7890;
        services.AddDesktopAuthentication(settings.ApiBaseUrl, redirectPort);

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

        return builder.Build();
    }
}

public sealed record FrontendSettings(Uri ApiBaseUrl);
