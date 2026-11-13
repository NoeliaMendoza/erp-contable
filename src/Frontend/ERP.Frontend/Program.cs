using System.Globalization;
using Avalonia;

namespace ERP.Frontend;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Interfaz en español de Ecuador; moneda USD. Los instantes auditables viajan en UTC desde la API.
        var culture = CultureInfo.GetCultureInfo("es-EC");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Usado también por el diseñador de Avalonia.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
