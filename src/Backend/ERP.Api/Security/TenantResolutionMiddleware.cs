using ERP.BuildingBlocks.Security;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Tenancy;
using Serilog.Context;

namespace ERP.Api.Security;

/// <summary>
/// Construye el contexto de tenant confiable de la solicitud:
/// <list type="number">
/// <item>toma el usuario de la identidad autenticada (claim <c>sub</c>);</item>
/// <item>trata <c>X-Tenant-Id</c> solo como una <b>selección</b> del cliente;</item>
/// <item>valida membresía activa en BD (con RLS) y carga permisos efectivos;</item>
/// <item>si no hay membresía responde 403 sin revelar si la empresa existe.</item>
/// </list>
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string TenantHeader = "X-Tenant-Id";

    public async Task InvokeAsync(HttpContext context, TenantSession session, ITenantAccessResolver resolver, IProblemDetailsService problems)
    {
        if (context.User.Identity?.IsAuthenticated == true && Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
        {
            session.SetUser(userId);
        }

        var requiresTenant = context.GetEndpoint()?.Metadata.GetMetadata<IRequiresTenant>() is not null;
        var header = context.Request.Headers[TenantHeader].ToString();

        // Sin usuario autenticado: la autorización responderá 401.
        if (session.UserId is not { } uid)
        {
            await next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(header))
        {
            if (requiresTenant)
            {
                await WriteProblemAsync(context, problems, StatusCodes.Status400BadRequest, "Empresa no seleccionada",
                    $"La operación requiere la cabecera {TenantHeader} con una empresa a la que pertenezca.");
                return;
            }

            await next(context);
            return;
        }

        if (!Guid.TryParse(header, out var tenantId))
        {
            await WriteProblemAsync(context, problems, StatusCodes.Status400BadRequest, "Empresa inválida", $"{TenantHeader} no es un identificador válido.");
            return;
        }

        session.SetCandidateTenant(tenantId);
        var access = await resolver.ResolveAsync(uid, tenantId, context.RequestAborted);
        if (!access.Granted)
        {
            session.ClearTenant();
            await WriteProblemAsync(context, problems, StatusCodes.Status403Forbidden, "Acceso denegado",
                "No tiene una membresía activa en la empresa seleccionada.");
            return;
        }

        session.ConfirmTenant(tenantId, access.Permissions);

        using (LogContext.PushProperty("TenantId", tenantId))
        {
            await next(context);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, IProblemDetailsService problems, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = { Status = status, Title = title, Detail = detail },
        });
    }
}
