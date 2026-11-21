using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Sales.Controllers;

internal static class ValidationExtensions
{
    public static async Task<IActionResult?> ValidateAsync<T>(
        this ControllerBase controller,
        IValidator<T> validator,
        T request,
        CancellationToken ct)
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
