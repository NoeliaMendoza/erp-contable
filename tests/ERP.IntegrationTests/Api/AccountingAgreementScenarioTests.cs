using System.Net;
using System.Net.Http.Json;
using ERP.DbMigrator;
using ERP.Infrastructure.Tenancy;
using ERP.IntegrationTests.Infrastructure;
using ERP.Modules.Accounting.Contracts;
using ERP.Modules.Accounting.Dtos;
using ERP.Tributario.Tax;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.IntegrationTests.Api;

/// <summary>
/// Recorrido del ACUERDO-02 contra la implementación real: compra de 30, venta de 20 y costo de 12,
/// sin IVA y con IVA general del 15% resuelto por catálogo (nunca una constante del módulo).
/// Las cuentas usan los códigos del acuerdo y se crean en el plan de la empresa antes de contabilizar.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountingAgreementScenarioTests(PostgresFixture db) : IAsyncLifetime
{
    private const string Caja = "1.1.01";
    private const string Inventario = "1.1.02";
    private const string IvaCompras = "1.1.03";
    private const string IvaVentas = "2.1.01";
    private const string Ventas = "4.1.01";
    private const string CostoVentas = "5.1.01";

    private ErpApiFactory _api = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await AccountingSchemaPrerequisite.RequireAsync(db, Ct);
        _api = new ErpApiFactory(db);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    /// <summary>Catálogo aislado: el porcentaje viene del catálogo, con vigencia y fuente, no del código.</summary>
    private static TaxRateCatalog Catalog(decimal percentage) => new("pruebas-acc03", [
        new(TaxKind.Iva, "2", $"IVA {percentage:0.##}% (valor de prueba)", percentage,
            new DateOnly(2026, 1, 1), null,
            "Catalogo sintetico de pruebas ACC-03; la tarifa oficial se verifica por fecha y documento.",
            VerificationStatus.Sintetico),
    ]);

    private async Task PlanAsync(HttpClient client)
    {
        foreach (var (code, name, kind, balance) in new[]
        {
            (Caja, "Caja", "Asset", "Debit"),
            (Inventario, "Inventario", "Asset", "Debit"),
            (IvaCompras, "IVA compras recuperable", "Asset", "Debit"),
            (IvaVentas, "IVA ventas por pagar", "Liability", "Credit"),
            (Ventas, "Ventas", "Income", "Credit"),
            (CostoVentas, "Costo de ventas", "Expense", "Debit"),
        })
        {
            using var response = await client.PostAsJsonAsync("/api/v1/accounts",
                new SaveAccountRequest(code, name, kind, balance, null, true), Ct);
            // Conflict cuando otra prueba de la misma empresa ya creó el plan: el estado buscado es el mismo.
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        }
    }

    private async Task<PostingResult> PostAsync(Guid tenantId, PostingRequest request)
    {
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantSession>().ConfirmTenant(tenantId, []);
        return await scope.ServiceProvider.GetRequiredService<IAccountingPostingService>().PostAsync(request, Ct);
    }

    /// <summary>
    /// Arma compra y venta del acuerdo. Replica el helper documentado: la tarifa se resuelve del catálogo
    /// y la línea de impuesto se omite cuando el importe es cero, porque una línea no admite debe y haber en cero.
    /// </summary>
    private static (PostingRequest Purchase, PostingRequest Sale) Scenario(
        TaxRateCatalog catalog, DateOnly taxDate, Guid purchaseId, Guid saleId)
    {
        var rate = catalog.Resolve(TaxKind.Iva, "2", taxDate);
        var purchaseVat = decimal.Round(30m * rate.Percentage / 100m, 2, MidpointRounding.AwayFromZero);
        var saleVat = decimal.Round(20m * rate.Percentage / 100m, 2, MidpointRounding.AwayFromZero);

        var purchaseLines = new List<PostingLine> { new(Inventario, 30m, 0m, "Inventario") };
        if (purchaseVat > 0m)
        {
            purchaseLines.Add(new(IvaCompras, purchaseVat, 0m, "IVA compras recuperable"));
        }

        purchaseLines.Add(new(Caja, 0m, 30m + purchaseVat, "Caja"));

        var saleLines = new List<PostingLine>
        {
            new(Caja, 20m + saleVat, 0m, "Caja"),
            new(Ventas, 0m, 20m, "Ventas"),
        };
        if (saleVat > 0m)
        {
            saleLines.Add(new(IvaVentas, 0m, saleVat, "IVA ventas por pagar"));
        }

        saleLines.Add(new(CostoVentas, 12m, 0m, "Costo de ventas"));
        saleLines.Add(new(Inventario, 0m, 12m, "Inventario"));

        return (
            new PostingRequest($"purchases:receipt:{purchaseId:D}", "purchases", "receipt", purchaseId, taxDate,
                "Compra del recorrido del acuerdo", purchaseLines),
            new PostingRequest($"sales:invoice:{saleId:D}", "sales", "invoice", saleId, taxDate,
                "Venta del recorrido del acuerdo", saleLines));
    }

    [Fact]
    public async Task Compra_30_venta_20_y_costo_12_sin_IVA_se_contabilizan_cuadrados()
    {
        using var client = _api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);
        await PlanAsync(client);
        var (purchase, sale) = Scenario(Catalog(0m), new DateOnly(2026, 3, 15), Guid.CreateVersion7(), Guid.CreateVersion7());

        var purchased = await PostAsync(DevelopmentSeeder.TenantAndinaId, purchase);
        var sold = await PostAsync(DevelopmentSeeder.TenantAndinaId, sale);

        var compra = await client.GetFromJsonAsync<JournalEntryResponse>($"/api/v1/journal-entries/{purchased.JournalEntryId}", Ct);
        Assert.Equal(30.00m, compra!.TotalDebit);
        Assert.Equal(30.00m, compra.TotalCredit);
        Assert.Equal([Inventario, Caja], compra.Lines.Select(l => l.AccountCode));
        Assert.Equal("purchases", compra.SourceModule);

        var venta = await client.GetFromJsonAsync<JournalEntryResponse>($"/api/v1/journal-entries/{sold.JournalEntryId}", Ct);
        Assert.Equal(32.00m, venta!.TotalDebit);
        Assert.Equal(32.00m, venta.TotalCredit);

        // Con impuesto cero no se emite la línea de IVA: ingreso, costo y salida de inventario viajan juntos.
        Assert.Equal([Caja, Ventas, CostoVentas, Inventario], venta.Lines.Select(l => l.AccountCode));
        Assert.Equal([20.00m, 0m, 12.00m, 0m], venta.Lines.Select(l => l.Debit));
        Assert.Equal([0m, 20.00m, 0m, 12.00m], venta.Lines.Select(l => l.Credit));
        Assert.Equal([1, 2, 3, 4], venta.Lines.Select(l => l.LineNumber));
        Assert.DoesNotContain(IvaVentas, venta.Lines.Select(l => l.AccountCode));
    }

    [Fact]
    public async Task El_mismo_recorrido_con_IVA_del_15_separa_el_impuesto_del_ingreso_y_del_costo()
    {
        using var client = _api.ClientFor(DevelopmentSeeder.BrunoEmail, DevelopmentSeeder.TenantPacificoId);
        await PlanAsync(client);
        var purchaseId = Guid.CreateVersion7();
        var saleId = Guid.CreateVersion7();
        var (purchase, sale) = Scenario(Catalog(15m), new DateOnly(2026, 3, 15), purchaseId, saleId);

        var purchased = await PostAsync(DevelopmentSeeder.TenantPacificoId, purchase);
        var sold = await PostAsync(DevelopmentSeeder.TenantPacificoId, sale);

        // Compra: base 30, IVA recuperable 4.50, total pagado 34.50. El IVA no engrosa el costo del inventario.
        var compra = await client.GetFromJsonAsync<JournalEntryResponse>($"/api/v1/journal-entries/{purchased.JournalEntryId}", Ct);
        Assert.Equal(34.50m, compra!.TotalDebit);
        Assert.Equal(34.50m, compra.TotalCredit);
        Assert.Equal([Inventario, IvaCompras, Caja], compra.Lines.Select(l => l.AccountCode));
        Assert.Equal(30.00m, compra.Lines[0].Debit);
        Assert.Equal(4.50m, compra.Lines[1].Debit);
        Assert.Equal(34.50m, compra.Lines[2].Credit);

        // Venta: cobro 23.00 (base 20 + IVA 3.00) y asiento completo de 35.00 al sumar el costo de 12.
        var venta = await client.GetFromJsonAsync<JournalEntryResponse>($"/api/v1/journal-entries/{sold.JournalEntryId}", Ct);
        Assert.Equal(35.00m, venta!.TotalDebit);
        Assert.Equal(35.00m, venta.TotalCredit);
        Assert.Equal([Caja, Ventas, IvaVentas, CostoVentas, Inventario], venta.Lines.Select(l => l.AccountCode));
        Assert.Equal(23.00m, venta.Lines[0].Debit);
        Assert.Equal(20.00m, venta.Lines[1].Credit);
        Assert.Equal(3.00m, venta.Lines[2].Credit);
        Assert.Equal(12.00m, venta.Lines[3].Debit);
        Assert.Equal(12.00m, venta.Lines[4].Credit);

        // El IVA forma parte del contenido idempotente: el mismo documento devuelve el mismo asiento,
        // y la misma clave con otro impuesto es conflicto, no un asiento nuevo.
        var retry = await PostAsync(DevelopmentSeeder.TenantPacificoId, sale);
        Assert.True(retry.AlreadyProcessed);
        Assert.Equal(sold.JournalEntryId, retry.JournalEntryId);
        var (_, otraTarifa) = Scenario(Catalog(12m), new DateOnly(2026, 3, 15), purchaseId, saleId);
        var conflict = await Assert.ThrowsAsync<ERP.BuildingBlocks.Domain.ConflictException>(
            () => PostAsync(DevelopmentSeeder.TenantPacificoId, otraTarifa));
        Assert.Equal("posting_idempotency_conflict", conflict.Code);

        // La trazabilidad documento -> asiento se conserva en el libro de la empresa.
        Assert.Equal(saleId, venta.SourceDocumentId);
        Assert.Equal("invoice", venta.SourceDocumentType);
    }

    /// <summary>
    /// Fija el bloqueo documentado en docs/modulos/acc03-integracion-ventas.md: los nombres de cuenta que
    /// <c>SaleService.ConfirmAsync</c> envía hoy se rechazan con <c>posting_account_not_available</c>, incluso
    /// con el plan del acuerdo creado. La prueba falla cuando Ventas adopte los códigos, que es la señal de
    /// que el bloqueo se cerró (#36 publica la configuración de cuentas por empresa).
    /// </summary>
    [Theory]
    [InlineData("Caja")]
    [InlineData("Ventas")]
    [InlineData("IVA por pagar")]
    [InlineData("Costo de ventas")]
    [InlineData("Inventario")]
    public async Task Los_nombres_de_cuenta_que_envia_Ventas_hoy_se_rechazan(string nombre)
    {
        using var client = _api.ClientFor(DevelopmentSeeder.AnaEmail, DevelopmentSeeder.TenantAndinaId);
        await PlanAsync(client);
        var saleId = Guid.CreateVersion7();
        var request = new PostingRequest($"sales:invoice:{saleId:D}", "sales", "invoice", saleId,
            new DateOnly(2026, 3, 15), "Venta con nombres de cuenta",
            [new PostingLine(nombre, 20m, 0m, nombre), new PostingLine(Ventas, 0m, 20m, "Ventas")]);

        var error = await Assert.ThrowsAsync<ERP.BuildingBlocks.Domain.DomainException>(
            () => PostAsync(DevelopmentSeeder.TenantAndinaId, request));

        Assert.Equal("posting_account_not_available", error.Code);
    }
}
