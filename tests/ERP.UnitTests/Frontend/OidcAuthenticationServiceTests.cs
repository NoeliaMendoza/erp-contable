using ERP.Frontend.Shared.Session;
using ERP.Frontend.Shared.Session.Oidc;

namespace ERP.UnitTests.Frontend;

/// <summary>Lógica de sesión del escritorio (PLAT-01) sin navegador: cancelación, renovación, expiración sin bucles y cierre.</summary>
public sealed class OidcAuthenticationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Inicio_exitoso_entrega_el_token_sin_renovar()
    {
        var oidc = new FakeOidc { SignIn = _ => Tokens("a1", minutes: 15, refresh: "r1") };
        using var service = new OidcAuthenticationService(oidc, new FixedClock(Now));

        var result = await service.SignInAsync(Ct);

        Assert.True(result.Succeeded);
        Assert.True(service.IsAuthenticated);
        Assert.Equal("a1", await service.GetAccessTokenAsync(Ct));
        Assert.Equal(0, oidc.RefreshCalls);
    }

    [Fact]
    public async Task Cancelar_desde_la_aplicacion_devuelve_Cancelled_sin_sesion()
    {
        var oidc = new FakeOidc { SignIn = ct => throw new OperationCanceledException(ct) };
        using var service = new OidcAuthenticationService(oidc, new FixedClock(Now));

        var result = await service.SignInAsync(Ct);

        Assert.Equal(SignInStatus.Cancelled, result.Status);
        Assert.False(service.IsAuthenticated);
        Assert.Null(await service.GetAccessTokenAsync(Ct));
    }

    [Fact]
    public async Task Cancelar_en_el_navegador_access_denied_devuelve_Cancelled()
    {
        var oidc = new FakeOidc { SignIn = _ => throw new OidcSignInException("Inicio de sesión cancelado.", cancelled: true) };
        using var service = new OidcAuthenticationService(oidc, new FixedClock(Now));

        var result = await service.SignInAsync(Ct);

        Assert.Equal(SignInStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Servidor_inaccesible_devuelve_Failed_con_mensaje_para_el_usuario()
    {
        var oidc = new FakeOidc { SignIn = _ => throw new OidcSignInException("No se pudo contactar al servidor de inicio de sesión.") };
        using var service = new OidcAuthenticationService(oidc, new FixedClock(Now));

        var result = await service.SignInAsync(Ct);

        Assert.Equal(SignInStatus.Failed, result.Status);
        Assert.Contains("servidor", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_por_vencer_se_renueva_una_sola_vez_con_solicitudes_simultaneas()
    {
        var gate = new TaskCompletionSource();
        var oidc = new FakeOidc
        {
            SignIn = _ => Tokens("a1", minutes: 0.5, refresh: "r1"),
            Refresh = async (_, _) =>
            {
                await gate.Task;
                return new OidcTokens("a2", Now.AddMinutes(15), "r2");
            },
        };
        using var service = new OidcAuthenticationService(oidc, new FixedClock(Now));
        await service.SignInAsync(Ct);

        var first = service.GetAccessTokenAsync(Ct).AsTask();
        var second = service.GetAccessTokenAsync(Ct).AsTask();
        gate.SetResult();

        Assert.Equal(["a2", "a2"], await Task.WhenAll(first, second));
        Assert.Equal(1, oidc.RefreshCalls);
    }

    [Fact]
    public async Task Renovacion_rechazada_termina_la_sesion_una_vez_como_expirada()
    {
        var oidc = new FakeOidc
        {
            SignIn = _ => Tokens("a1", minutes: 0.5, refresh: "r1"),
            Refresh = (_, _) => throw new OidcSignInException("La sesión expiró."),
        };
        using var service = new OidcAuthenticationService(oidc, new FixedClock(Now));
        var ended = new List<SessionEndReason>();
        service.SessionEnded += (_, e) => ended.Add(e.Reason);
        await service.SignInAsync(Ct);

        Assert.Null(await service.GetAccessTokenAsync(Ct));
        Assert.Null(await service.GetAccessTokenAsync(Ct));

        Assert.Equal([SessionEndReason.Expired], ended);
        Assert.Equal(1, oidc.RefreshCalls);
        Assert.False(service.IsAuthenticated);
    }

    [Fact]
    public async Task Token_vencido_sin_renovacion_termina_la_sesion()
    {
        var oidc = new FakeOidc { SignIn = _ => Tokens("a1", minutes: -1, refresh: null) };
        using var service = new OidcAuthenticationService(oidc, new FixedClock(Now));
        var ended = 0;
        service.SessionEnded += (_, _) => ended++;
        await service.SignInAsync(Ct);

        Assert.Null(await service.GetAccessTokenAsync(Ct));
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task Varios_401_solo_terminan_la_sesion_una_vez()
    {
        var oidc = new FakeOidc { SignIn = _ => Tokens("a1", minutes: 15, refresh: "r1") };
        using var service = new OidcAuthenticationService(oidc, new FixedClock(Now));
        var ended = 0;
        service.SessionEnded += (_, _) => ended++;
        await service.SignInAsync(Ct);

        service.ReportUnauthorized("a1");
        service.ReportUnauthorized("a1");

        Assert.Equal(1, ended);
        Assert.False(service.IsAuthenticated);
    }

    [Fact]
    public async Task Un_401_tardio_de_una_sesion_anterior_no_cierra_la_nueva()
    {
        var issued = 0;
        var oidc = new FakeOidc { SignIn = _ => Tokens($"a{++issued}", minutes: 15, refresh: null) };
        using var service = new OidcAuthenticationService(oidc, new FixedClock(Now));
        await service.SignInAsync(Ct);
        await service.SignOutAsync(Ct);
        await service.SignInAsync(Ct);

        service.ReportUnauthorized("a1");

        Assert.True(service.IsAuthenticated);
        Assert.Equal("a2", await service.GetAccessTokenAsync(Ct));
    }

    [Fact]
    public async Task Cerrar_sesion_descarta_tokens_revoca_la_renovacion_y_avisa()
    {
        var oidc = new FakeOidc { SignIn = _ => Tokens("a1", minutes: 15, refresh: "r1") };
        using var service = new OidcAuthenticationService(oidc, new FixedClock(Now));
        var ended = new List<SessionEndReason>();
        service.SessionEnded += (_, e) => ended.Add(e.Reason);
        await service.SignInAsync(Ct);

        await service.SignOutAsync(Ct);

        Assert.False(service.IsAuthenticated);
        Assert.Null(await service.GetAccessTokenAsync(Ct));
        Assert.Equal(["r1"], oidc.Revoked);
        Assert.Equal([SessionEndReason.SignedOut], ended);
    }

    [Fact]
    public async Task Cerrar_sesion_funciona_aunque_el_servidor_no_responda()
    {
        var oidc = new FakeOidc
        {
            SignIn = _ => Tokens("a1", minutes: 15, refresh: "r1"),
            Revoke = (_, _) => throw new HttpRequestException("sin red"),
        };
        using var service = new OidcAuthenticationService(oidc, new FixedClock(Now));
        await service.SignInAsync(Ct);

        await service.SignOutAsync(Ct);

        Assert.False(service.IsAuthenticated);
    }

    private static Task<OidcTokens> Tokens(string access, double minutes, string? refresh) =>
        Task.FromResult(new OidcTokens(access, Now.AddMinutes(minutes), refresh));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeOidc : IOidcTokenClient
    {
        public Func<CancellationToken, Task<OidcTokens>> SignIn { get; init; } = _ => throw new NotSupportedException();

        public Func<string, CancellationToken, Task<OidcTokens>> Refresh { get; init; } = (_, _) => throw new NotSupportedException();

        public Func<string, CancellationToken, Task> Revoke { get; init; } = (_, _) => Task.CompletedTask;

        public int RefreshCalls { get; private set; }

        public List<string> Revoked { get; } = [];

        public Task<OidcTokens> SignInInteractivelyAsync(CancellationToken cancellationToken) => SignIn(cancellationToken);

        public Task<OidcTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            RefreshCalls++;
            return Refresh(refreshToken, cancellationToken);
        }

        public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
        {
            Revoked.Add(refreshToken);
            await Revoke(refreshToken, cancellationToken);
        }
    }
}
