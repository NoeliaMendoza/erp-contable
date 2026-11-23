using ERP.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ERP.IntegrationTests.Infrastructure;

/// <summary>Levanta la API real (Program) contra la BD del fixture usando el rol runtime erp_app.</summary>
public sealed class ErpApiFactory(PostgresFixture database) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ErpRuntime", database.AppConnectionString);
        builder.UseSetting("Authentication:DevelopmentHeader:Enabled", "true");

        // Igual que en Development: si un servicio registrado no puede construirse (falta una dependencia),
        // la API no arranca y todas las pruebas fallan, en lugar de descubrirlo al ejecutar la aplicación.
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });
    }

    /// <summary>Cliente autenticado como un usuario semilla y, opcionalmente, con una empresa seleccionada.</summary>
    public HttpClient ClientFor(string? email, Guid? tenantId = null)
    {
        var client = CreateClient();
        if (email is not null)
        {
            client.DefaultRequestHeaders.Add(AuthenticationSetup.DevelopmentUserHeader, email);
        }

        if (tenantId is not null)
        {
            client.DefaultRequestHeaders.Add(TenantResolutionMiddleware.TenantHeader, tenantId.Value.ToString());
        }

        return client;
    }
}
