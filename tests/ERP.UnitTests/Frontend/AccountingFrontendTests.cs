using System.Net;
using System.Text;
using System.Text.Json;
using ERP.Frontend.Modules.Accounting;
using ERP.Frontend.Modules.Accounting.ViewModels;
using ERP.Frontend.Shared.Api;
using ERP.Frontend.Shared.Session;

namespace ERP.UnitTests.Frontend;

public sealed class AccountingFrontendTests
{
    private const string EmptyPage = """{"items":[],"totalCount":0,"page":1,"pageSize":20}""";

    private static readonly AccountItem Account = new(Guid.NewGuid(), "01.01", "Caja", "Asset", "Debit", null, true, true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Listado_codifica_busqueda_y_usa_filtros_y_empresa_de_sesion()
    {
        string? url = null;
        string? tenantHeader = null;
        var session = Session(true);
        using var handler = new SessionHeadersHandler(session)
        {
            InnerHandler = new StubHandler(req =>
            {
                url = req.RequestUri!.PathAndQuery;
                tenantHeader = req.Headers.GetValues("X-Tenant-Id").Single();
                return Json(HttpStatusCode.OK, EmptyPage);
            }),
        };
        var client = new AccountingApi(new ErpApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") }));

        await client.ListAsync(2, 20, " 01 & caja ", false, Ct);

        Assert.Equal("/api/v1/accounts?page=2&pageSize=20&search=01%20%26%20caja&isActive=false", url);
        Assert.Equal(session.TenantId.ToString(), tenantHeader);
    }

    [Fact]
    public async Task Formulario_rechaza_datos_invalidos_sin_enviar()
    {
        var requests = new List<HttpMethod>();
        var vm = CreateViewModel(req =>
        {
            requests.Add(req.Method);
            return Json(HttpStatusCode.OK, EmptyPage);
        });
        await vm.LoadCommand.ExecutionTask!;
        requests.Clear();
        vm.Code = "con espacios";
        vm.Name = "";
        vm.FormKind = null;
        vm.FormBalance = null;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.CodeError);
        Assert.NotNull(vm.NameError);
        Assert.NotNull(vm.KindError);
        Assert.NotNull(vm.NormalBalanceError);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task Guarda_todos_los_campos_y_muestra_errores_junto_al_campo()
    {
        string? body = null;
        var vm = CreateViewModel(req =>
        {
            if (req.Method == HttpMethod.Post)
            {
                body = req.Content!.ReadAsStringAsync(Ct).GetAwaiter().GetResult();
                return Json(HttpStatusCode.BadRequest, """{"status":400,"errors":{"Code":["Código inválido"],"ParentId":["Padre inválido"]}}""");
            }

            return Json(HttpStatusCode.OK, EmptyPage);
        });
        await vm.LoadCommand.ExecutionTask!;
        var parent = Guid.NewGuid();
        vm.Code = "01.01";
        vm.Name = "Caja";
        vm.FormKind = AccountOption.Kinds.Single(o => o.Value == "Asset");
        vm.FormBalance = AccountOption.Balances.Single(o => o.Value == "Debit");
        vm.FormParent = new(parent, "01 - Activos");
        vm.AllowsPosting = false;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("01.01", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("Caja", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("Asset", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("Debit", json.RootElement.GetProperty("normalBalance").GetString());
        Assert.Equal(parent, json.RootElement.GetProperty("parentId").GetGuid());
        Assert.False(json.RootElement.GetProperty("allowsPosting").GetBoolean());
        Assert.Equal("Código inválido", vm.CodeError);
        Assert.Equal("Padre inválido", vm.ParentIdError);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task Selector_carga_todas_las_paginas_y_excluye_la_cuenta_y_descendientes()
    {
        var child = Account with { Id = Guid.NewGuid(), Code = "01.01.01", ParentId = Account.Id };
        var grandchild = Account with { Id = Guid.NewGuid(), Code = "01.01.01.01", ParentId = child.Id, IsActive = false };
        var independent = Account with { Id = Guid.NewGuid(), Code = "02", ParentId = null };
        var vm = CreateViewModel(req =>
        {
            var items = req.RequestUri!.Query.Contains("pageSize=100", StringComparison.Ordinal)
                ? req.RequestUri.Query.Contains("page=2&", StringComparison.Ordinal)
                    ? new[] { grandchild, independent }
                    : new[] { Account, child }
                : new[] { Account };
            var page = req.RequestUri.Query.Contains("page=2&", StringComparison.Ordinal) ? 2 : 1;
            // Dos páginas de padres; no depender de que el selector tenga <= 100 cuentas.
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { items, totalCount = 101, page, pageSize = 100 }));
        });
        await vm.LoadCommand.ExecutionTask!;

        vm.EditCommand.Execute(Account);

        Assert.DoesNotContain(vm.Parents, p => p.Id == Account.Id || p.Id == child.Id || p.Id == grandchild.Id);
        Assert.Contains(vm.Parents, p => p.Id == independent.Id);
        Assert.Contains(vm.Parents, p => p.Id is null);
    }

    [Fact]
    public async Task Desactivar_exige_confirmacion_y_cancelar_no_envia()
    {
        var patches = 0;
        var vm = CreateViewModel(req =>
        {
            if (req.Method == HttpMethod.Patch)
            {
                patches++;
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(Account with { IsActive = false }));
            }

            return Json(HttpStatusCode.OK, EmptyPage);
        });
        await vm.LoadCommand.ExecutionTask!;

        vm.AskToggleCommand.Execute(Account);
        Assert.NotNull(vm.PendingToggle);
        Assert.Equal(0, patches);
        vm.CancelToggleCommand.Execute(null);
        Assert.Null(vm.PendingToggle);
        Assert.Equal(0, patches);
        vm.AskToggleCommand.Execute(Account);
        await vm.ConfirmToggleCommand.ExecuteAsync(null);

        Assert.Equal(1, patches);
        Assert.Null(vm.PendingToggle);
        Assert.NotNull(vm.SuccessMessage);
    }

    [Fact]
    public async Task Error_al_desactivar_permanece_visible_y_no_recarga()
    {
        var vm = CreateViewModel(req => req.Method == HttpMethod.Patch
            ? Json(HttpStatusCode.Conflict, """{"status":409,"detail":"Conflicto de prueba"}""")
            : Json(HttpStatusCode.OK, EmptyPage));
        await vm.LoadCommand.ExecutionTask!;
        vm.AskToggleCommand.Execute(Account);

        await vm.ConfirmToggleCommand.ExecuteAsync(null);

        Assert.Equal("Conflicto de prueba", vm.ErrorMessage);
        Assert.NotNull(vm.PendingToggle);
    }

    [Fact]
    public async Task Usuario_sin_permiso_de_escritura_no_envia_cambios()
    {
        var methods = new List<HttpMethod>();
        var vm = CreateViewModel(req =>
        {
            methods.Add(req.Method);
            return Json(HttpStatusCode.OK, EmptyPage);
        }, canWrite: false);
        await vm.LoadCommand.ExecutionTask!;
        methods.Clear();

        vm.Code = "01";
        vm.Name = "Caja";
        await vm.SaveCommand.ExecuteAsync(null);
        vm.AskToggleCommand.Execute(Account);
        await vm.ConfirmToggleCommand.ExecuteAsync(null);

        Assert.False(vm.CanWrite);
        Assert.Null(vm.PendingToggle);
        Assert.Empty(methods);
    }

    [Fact]
    public async Task Operacion_en_curso_impide_cambiar_formulario_pagina_y_enviar_otro_comando()
    {
        var requests = 0;
        var vm = CreateViewModel(_ =>
        {
            requests++;
            return Json(HttpStatusCode.OK, EmptyPage);
        });
        await vm.LoadCommand.ExecutionTask!;
        requests = 0;
        vm.Code = "01";
        vm.Name = "Caja sin guardar";
        vm.Page = 2;
        vm.TotalPages = 3;
        vm.IsBusy = true;

        await vm.SaveCommand.ExecuteAsync(null);
        await vm.SearchNowCommand.ExecuteAsync(null);
        await vm.NextPageCommand.ExecuteAsync(null);
        await vm.PreviousPageCommand.ExecuteAsync(null);
        vm.EditCommand.Execute(Account);
        vm.NewAccountCommand.Execute(null);
        vm.AskToggleCommand.Execute(Account);

        Assert.Equal(0, requests);
        Assert.Equal(2, vm.Page);
        Assert.Equal("01", vm.Code);
        Assert.Equal("Caja sin guardar", vm.Name);
        Assert.Null(vm.PendingToggle);
    }

    [Fact]
    public async Task Edicion_usa_PUT_y_conserva_el_padre_actual()
    {
        string? path = null;
        var parent = Account with { Id = Guid.NewGuid(), Code = "01" };
        var child = Account with { ParentId = parent.Id };
        var vm = CreateViewModel(req =>
        {
            if (req.Method == HttpMethod.Put)
            {
                path = req.RequestUri!.AbsolutePath;
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(child));
            }

            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { items = new[] { parent, child }, totalCount = 2, page = 1, pageSize = 100 }));
        });
        await vm.LoadCommand.ExecutionTask!;
        vm.EditCommand.Execute(child);
        Assert.Equal(parent.Id, vm.FormParent!.Id);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal($"/api/v1/accounts/{child.Id}", path);
        Assert.Null(vm.Editing);
        Assert.NotNull(vm.SuccessMessage);
    }

    private static SessionState Session(bool canWrite)
    {
        var session = new SessionState();
        session.SignIn("ana.admin@demo.local");
        session.SelectTenant(Guid.NewGuid(), "Empresa ficticia");
        session.SetTenantDetails(canWrite ? ["accounting.accounts.read", "accounting.accounts.write"] : ["accounting.accounts.read"], ["accounting"]);
        return session;
    }

    private static AccountsViewModel CreateViewModel(Func<HttpRequestMessage, HttpResponseMessage> responder, bool canWrite = true)
    {
        var api = new ErpApiClient(new HttpClient(new StubHandler(responder)) { BaseAddress = new Uri("https://api.test/") });
        return new AccountsViewModel(new AccountingApi(api), Session(canWrite));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
