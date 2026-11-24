using ERP.BuildingBlocks.Paging;
using ERP.Infrastructure.Web;
using ERP.Modules.Accounting.Dtos;
using ERP.Modules.Accounting.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Modules.Accounting.Controllers;

/// <summary>
/// Libro diario de la empresa activa, con filtro por fecha contable. Solo lectura:
/// no se expone un endpoint público de contabilización ni de borrado de asientos.
/// </summary>
[Route(ApiRoutes.V1 + "/journal-entries")]
[Tags("Accounting")]
public sealed class JournalEntriesController(JournalService service) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(AccountingPermissions.EntriesRead)]
    public Task<PagedResult<JournalEntryResponse>> List(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken ct = default) =>
        service.ListAsync(new PageRequest(page, pageSize), from, to, ct);

    [HttpGet("{id:guid}")]
    [RequirePermission(AccountingPermissions.EntriesRead)]
    public Task<JournalEntryResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);
}
