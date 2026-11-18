using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Inventory.Controllers;

internal static class ValidationExtensions
{
    /// <summary>Valida con FluentValidation y devuelve 400 ProblemDetails con errores por campo, o null si es válido.</summary>
    public static async Task<IActionResult?> ValidateAsync<T>(this ControllerBase controller, IValidator<T> validator, T request, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(request, ct);
        if (result.IsValid)
        {
            return null;
        }

        foreach (var error in result.Errors)
        {
            controller.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        return controller.ValidationProblem(controller.ModelState);
    }
}
