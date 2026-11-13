using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Diagnostics;

namespace ERP.Api.Errors;

/// <summary>Traduce errores de dominio a ProblemDetails con un código estable (<c>code</c>).</summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, code) = exception switch
        {
            NotFoundException nf => (StatusCodes.Status404NotFound, "Recurso no encontrado", nf.Code),
            DomainException de => (StatusCodes.Status422UnprocessableEntity, "Regla de negocio incumplida", de.Code),
            TenantContextMissingException => (StatusCodes.Status400BadRequest, "Empresa no seleccionada", "tenant_required"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "No autenticado", "unauthenticated"),
            _ => (0, string.Empty, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
                Extensions = { ["code"] = code },
            },
        });
    }
}
