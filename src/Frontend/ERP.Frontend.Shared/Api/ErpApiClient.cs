using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.Frontend.Shared.Session;

namespace ERP.Frontend.Shared.Api;

/// <summary>Error de la API traducido a un mensaje comprensible en español.</summary>
public sealed class ApiException(HttpStatusCode status, string message, ProblemDetailsModel? problem)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    public ProblemDetailsModel? Problem { get; } = problem;
}

/// <summary>Cliente HTTP tipado para /api/v1. Centraliza cabeceras de sesión y manejo de ProblemDetails.</summary>
public sealed class ErpApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<MyProfile> GetMyProfileAsync(CancellationToken ct = default) => GetAsync<MyProfile>("api/v1/me", ct);

    public Task<CurrentTenant> GetCurrentTenantAsync(CancellationToken ct = default) => GetAsync<CurrentTenant>("api/v1/tenant/current", ct);

    public Task<IReadOnlyList<Branch>> GetBranchesAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<Branch>>("api/v1/tenant/branches", ct);

    public Task<IReadOnlyList<Taxpayer>> GetTaxpayersAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<Taxpayer>>("api/v1/tenant/taxpayers", ct);

    public async Task<Branch> CreateBranchAsync(CreateBranch request, CancellationToken ct = default)
    {
        using var response = await SendAsync(() => http.PostAsJsonAsync("api/v1/tenant/branches", request, Json, ct));
        return await ReadAsync<Branch>(response, ct);
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await SendAsync(() => http.GetAsync(url, ct));
        return await ReadAsync<T>(response, ct);
    }

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable,
                "No se pudo conectar con el servidor del ERP. Verifique que la API esté en ejecución y la dirección configurada.", null)
            { HelpLink = ex.Message };
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }

        throw await ToExceptionAsync(response, ct);
    }

    internal static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        ProblemDetailsModel? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ProblemDetailsModel>(Json, ct);
        }
        catch (JsonException)
        {
            // Respuesta sin cuerpo ProblemDetails: se usa el mensaje genérico.
        }
        catch (NotSupportedException)
        {
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Sesión no válida. Vuelva a iniciar sesión.",
            HttpStatusCode.Forbidden => problem?.Detail ?? "No tiene permiso para esta operación en la empresa seleccionada.",
            HttpStatusCode.NotFound => problem?.Detail ?? "El recurso no existe o no pertenece a la empresa activa.",
            HttpStatusCode.BadRequest when problem?.Errors is { Count: > 0 } errors =>
                string.Join(Environment.NewLine, errors.SelectMany(e => e.Value)),
            _ => problem?.Detail ?? problem?.Title ?? $"Error del servidor ({(int)response.StatusCode}).",
        };

        return new ApiException(response.StatusCode, message, problem);
    }
}

/// <summary>Agrega a cada solicitud la identidad de desarrollo y la empresa seleccionada.</summary>
public sealed class SessionHeadersHandler(SessionState session) : DelegatingHandler
{
    public const string DevUserHeader = "X-Dev-User";
    public const string TenantHeader = "X-Tenant-Id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // TODO(PLAT-01): sustituir por Authorization: Bearer <token> obtenido con OIDC + PKCE.
        if (session.UserEmail is { Length: > 0 } email)
        {
            request.Headers.Remove(DevUserHeader);
            request.Headers.Add(DevUserHeader, email);
        }

        request.Headers.Remove(TenantHeader);
        if (session.TenantId is { } tenantId)
        {
            request.Headers.Add(TenantHeader, tenantId.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
