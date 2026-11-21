using ERP.BuildingBlocks.Paging;
using ERP.Infrastructure.Web;
using ERP.Modules.Accounting.Dtos;
using ERP.Modules.Accounting.Services;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Accounting.Controllers;

/// <summary>Plan de cuentas de la empresa activa.</summary>
[Route(ApiRoutes.V1 + "/accounts")]
[Tags("Accounting")]
public sealed class AccountsController(AccountService service) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(AccountingPermissions.AccountsRead)]
    public Task<PagedResult<AccountResponse>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? search = null, [FromQuery] bool? isActive = null, CancellationToken ct = default) =>
        service.ListAsync(new PageRequest(page, pageSize, search), isActive, ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(AccountingPermissions.AccountsRead)]
    public Task<AccountResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost]
    [RequirePermission(AccountingPermissions.AccountsWrite)]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveAccountRequest request, [FromServices] IValidator<SaveAccountRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(request, validator, ct) is { } invalid)
        {
            return invalid;
        }

        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(AccountingPermissions.AccountsWrite)]
    public async Task<IActionResult> Update(Guid id, SaveAccountRequest request, [FromServices] IValidator<SaveAccountRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(request, validator, ct) is { } invalid)
        {
            return invalid;
        }

        return Ok(await service.UpdateAsync(id, request, ct));
    }

    [HttpPatch("{id:guid}/status")]
    [RequirePermission(AccountingPermissions.AccountsWrite)]
    public async Task<IActionResult> ChangeStatus(Guid id, ChangeAccountStatusRequest request, CancellationToken ct)
    {
        if (request.IsActive is not { } isActive)
        {
            ModelState.AddModelError(nameof(request.IsActive), "Indique el estado de la cuenta.");
            return ValidationProblem(ModelState);
        }

        return Ok(await service.SetStatusAsync(id, isActive, ct));
    }

    private async Task<IActionResult?> ValidateAsync(SaveAccountRequest request, IValidator<SaveAccountRequest> validator, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(request, ct);
        if (result.IsValid)
        {
            return null;
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        return ValidationProblem(ModelState);
    }
}
