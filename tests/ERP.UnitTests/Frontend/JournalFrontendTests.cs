using System.Globalization;
using System.Net;
using System.Text;
using ERP.Frontend.Modules.Accounting;
using ERP.Frontend.Modules.Accounting.ViewModels;
using ERP.Frontend.Shared.Api;

namespace ERP.UnitTests.Frontend;

/// <summary>
/// Pantalla de libro diario (ACC-03): solo consulta, con filtro por fecha contable y paginación.
/// Las pruebas no tocan la red: el controlador HTTP está simulado.
/// </summary>
public sealed class JournalFrontendTests
{
    private const string EmptyPage = """{"items":[],"totalCount":0,"page":1,"pageSize":20}""";

    private static string Entry(string id, string date, string description, string status = "Posted") => $$"""
        {
          "id": "{{id}}", "accountingDate": "{{date}}", "description": "{{description}}", "status": "{{status}}",
          "sourceModule": "sales", "sourceDocumentType": "invoice",
          "sourceDocumentId": "11111111-1111-1111-1111-111111111111",
          "postedAtUtc": "2026-03-15T10:00:00+00:00", "totalDebit": 32.20, "totalCredit": 32.20,
          "lines": [
            { "lineNumber": 1, "accountId": "22222222-2222-2222-2222-222222222222", "accountCode": "1.1.01",
              "accountName": "Caja", "debit": 32.20, "credit": 0, "memo": "Cobro" },
            { "lineNumber": 2, "accountId": "33333333-3333-3333-3333-333333333333", "accountCode": "4.1.01",
              "accountName": "Ventas", "debit": 0, "credit": 32.20, "memo": null }
          ]
        }
        """;

    private static string Page(string items, int totalCount, int page = 1, int pageSize = 20) =>
        $$"""{"items":[{{items}}],"totalCount":{{totalCount}},"page":{{page}},"pageSize":{{pageSize}}}""";

    [Fact]
    public async Task La_carga_inicial_pide_la_primera_pagina_sin_filtros_y_proyecta_el_asiento()
    {
        var urls = new List<string>();
        var vm = CreateViewModel(req =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return Json(HttpStatusCode.OK, Page(Entry("44444444-4444-4444-4444-444444444444", "2026-03-15", "Factura 001"), 1));
        });

        await vm.LoadCommand.ExecutionTask!;

        Assert.Equal("/api/v1/journal-entries?page=1&pageSize=20", Assert.Single(urls));
        var entry = Assert.Single(vm.Entries);
        Assert.Equal("Factura 001", entry.Description);
        Assert.Equal("Contabilizado", entry.StatusLabel);
        Assert.Equal("sales · invoice", entry.SourceLabel);
        Assert.Equal("15/03/2026", entry.DateLabel);
        Assert.Equal("1.1.01 - Caja", entry.Lines[0].Account);
        Assert.Equal(string.Empty, entry.Lines[0].CreditLabel);
        Assert.Equal(string.Empty, entry.Lines[1].MemoLabel);
        Assert.Equal(32.20m.ToString("N2", CultureInfo.CurrentCulture), entry.Lines[1].CreditLabel);
        Assert.False(vm.IsEmpty);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task El_filtro_de_fechas_viaja_en_la_consulta_y_vuelve_a_la_primera_pagina()
    {
        var urls = new List<string>();
        var vm = CreateViewModel(req =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return Json(HttpStatusCode.OK, EmptyPage);
        });
        await vm.LoadCommand.ExecutionTask!;
        vm.Page = 3;
        vm.From = new DateTime(2026, 2, 1, 23, 30, 0, DateTimeKind.Unspecified);
        vm.To = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Unspecified);

        await vm.FilterCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.Page);
        Assert.Equal("/api/v1/journal-entries?page=1&pageSize=20&from=2026-02-01&to=2026-02-28", urls[^1]);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task Un_rango_invertido_se_avisa_sin_llamar_a_la_API()
    {
        var requests = 0;
        var vm = CreateViewModel(_ =>
        {
            requests++;
            return Json(HttpStatusCode.OK, EmptyPage);
        });
        await vm.LoadCommand.ExecutionTask!;
        vm.From = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Unspecified);
        vm.To = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Unspecified);

        await vm.FilterCommand.ExecuteAsync(null);

        Assert.Equal(1, requests);
        Assert.Equal("La fecha inicial no puede ser posterior a la final.", vm.FilterError);
    }

    [Fact]
    public async Task Limpiar_filtros_borra_las_fechas_el_aviso_y_recarga()
    {
        var urls = new List<string>();
        var vm = CreateViewModel(req =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return Json(HttpStatusCode.OK, EmptyPage);
        });
        await vm.LoadCommand.ExecutionTask!;
        vm.From = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Unspecified);
        vm.To = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Unspecified);
        await vm.FilterCommand.ExecuteAsync(null);

        await vm.ClearFiltersCommand.ExecuteAsync(null);

        Assert.Null(vm.From);
        Assert.Null(vm.To);
        Assert.Null(vm.FilterError);
        Assert.Equal("/api/v1/journal-entries?page=1&pageSize=20", urls[^1]);
    }

    [Fact]
    public async Task La_paginacion_avanza_retrocede_y_respeta_los_limites()
    {
        var urls = new List<string>();
        var vm = CreateViewModel(req =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return Json(HttpStatusCode.OK, Page(Entry("55555555-5555-5555-5555-555555555555", "2026-03-15", "Factura"), 45));
        });
        await vm.LoadCommand.ExecutionTask!;

        Assert.Equal(3, vm.TotalPages);
        Assert.False(vm.CanGoPrevious);
        Assert.True(vm.CanGoNext);
        Assert.Equal("Página 1 de 3 · 45 asiento(s)", vm.PageLabel);

        await vm.PreviousPageCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Page);
        Assert.Single(urls);

        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Page);
        Assert.Equal("/api/v1/journal-entries?page=2&pageSize=20", urls[^1]);
        await vm.PreviousPageCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Page);
        Assert.Equal("/api/v1/journal-entries?page=1&pageSize=20", urls[^1]);
    }

    [Fact]
    public async Task Una_pagina_vacia_tras_filtrar_retrocede_a_la_ultima_con_datos()
    {
        var urls = new List<string>();
        var vm = CreateViewModel(req =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return req.RequestUri!.Query.Contains("page=4", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, Page(string.Empty, 21, 4))
                : Json(HttpStatusCode.OK, Page(Entry("66666666-6666-6666-6666-666666666666", "2026-03-15", "Factura"), 21, 2));
        });
        await vm.LoadCommand.ExecutionTask!;
        vm.Page = 4;

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Page);
        Assert.Single(vm.Entries);
        Assert.Equal("/api/v1/journal-entries?page=2&pageSize=20", urls[^1]);
    }

    [Fact]
    public async Task Un_error_de_la_API_se_muestra_y_no_deja_la_pantalla_como_vacia()
    {
        var vm = CreateViewModel(_ => Json(HttpStatusCode.UnprocessableEntity,
            """{"code":"journal_date_range_invalid","title":"Regla de negocio","detail":"La fecha inicial no puede ser posterior a la final."}"""));

        await vm.LoadCommand.ExecutionTask!;

        Assert.Equal("La fecha inicial no puede ser posterior a la final.", vm.ErrorMessage);
        Assert.False(vm.IsEmpty);
        Assert.Empty(vm.Entries);
    }

    private static JournalViewModel CreateViewModel(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var api = new ErpApiClient(new HttpClient(new StubHandler(responder)) { BaseAddress = new Uri("https://api.test/") });
        return new JournalViewModel(new JournalApi(api));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
