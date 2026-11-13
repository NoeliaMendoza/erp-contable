using ERP.Infrastructure.Web;
using ERP.Modules.Platform.Dtos;
using ERP.Modules.Platform.Services;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Platform.Controllers;

/// <summary>Sucursales de la empresa activa.</summary>
[Route(ApiRoutes.V1 + "/tenant/branches")]
[Tags("Platform")]
public sealed class BranchesController(PlatformService service) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(PlatformPermissions.BranchesRead)]
    public Task<IReadOnlyList<BranchResponse>> List(CancellationToken ct) => service.ListBranchesAsync(ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(PlatformPermissions.BranchesRead)]
    public Task<BranchResponse> Get(Guid id, CancellationToken ct) => service.GetBranchAsync(id, ct);

    [HttpPost]
    [RequirePermission(PlatformPermissions.BranchesManage)]
    [ProducesResponseType<BranchResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        CreateBranchRequest request,
        [FromServices] IValidator<CreateBranchRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var created = await service.CreateBranchAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }
}
