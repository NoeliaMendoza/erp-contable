using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Sales.Data;
using ERP.Modules.Sales.Dtos;
using ERP.Modules.Sales.Models;
using ERP.Modules.Sales.Validators;
using ERP.Tributario.Identification;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Modules.Sales.Services;

public sealed class CustomerService(SalesDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public Task<PagedResult<CustomerResponse>> ListAsync(PageRequest page, bool? isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var query = db.Customers.AsNoTracking();
            if (isActive is not null)
            {
                query = query.Where(c => c.IsActive == isActive);
            }
            if (page.Term is { } term)
            {
                var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                query = query.Where(c =>
                    EF.Functions.ILike(c.Name, pattern, "\\")
                    || EF.Functions.ILike(c.IdentificationNumber, pattern, "\\")
                    || (c.Email != null && EF.Functions.ILike(c.Email, pattern, "\\"))
                    || (c.Phone != null && EF.Functions.ILike(c.Phone, pattern, "\\")));
            }

            var total = await query.CountAsync(ct);
            // Una sola consulta por página; la advertencia de identificación se calcula en memoria con ToResponse.
            var customers = await query.OrderBy(c => c.Name).ThenBy(c => c.Id)
                .Skip(page.Skip).Take(page.SafePageSize)
                .ToListAsync(ct);
            var items = customers.Select(ToResponse).ToList();
            return new PagedResult<CustomerResponse>(items, total, page.SafePage, page.SafePageSize);
        }, cancellationToken);

    public Task<CustomerResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
            ToResponse(await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new NotFoundException("Cliente", id)),
            cancellationToken);

    public Task<CustomerResponse> CreateAsync(SaveCustomerRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var data = ParseData(request);
            var customer = Customer.Create(Guid.CreateVersion7(), tenant.RequireTenantId(), data, clock.GetUtcNow(), tenant.UserId);
            await EnsureUniqueAsync(customer, null, ct);
            db.Customers.Add(customer);
            await SaveAsync(customer, ct);
            return ToResponse(customer);
        }, cancellationToken);

    public Task<CustomerResponse> UpdateAsync(Guid id, SaveCustomerRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new NotFoundException("Cliente", id);
            var data = ParseData(request);
            customer.Update(data, clock.GetUtcNow(), tenant.UserId);
            await EnsureUniqueAsync(customer, id, ct);
            await SaveAsync(customer, ct);
            return ToResponse(customer);
        }, cancellationToken);

    public Task<CustomerResponse> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new NotFoundException("Cliente", id);
            customer.SetActive(isActive, clock.GetUtcNow(), tenant.UserId);
            await db.SaveChangesAsync(ct);
            return ToResponse(customer);
        }, cancellationToken);

    private async Task EnsureUniqueAsync(Customer customer, Guid? exceptId, CancellationToken ct)
    {
        var duplicate = await db.Customers.AnyAsync(c =>
            c.Id != exceptId
            && c.IdentificationType == customer.IdentificationType
            && c.IdentificationNumber == customer.IdentificationNumber, ct);
        if (duplicate)
        {
            throw new ConflictException("customer_identification_duplicated",
                $"Ya existe un cliente con el {customer.IdentificationType} '{customer.IdentificationNumber}' en esta empresa.");
        }
    }

    private async Task SaveAsync(Customer customer, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("customer_identification_duplicated",
                $"Ya existe un cliente con el {customer.IdentificationType} '{customer.IdentificationNumber}' en esta empresa.");
        }
    }

    private static CustomerData ParseData(SaveCustomerRequest request)
    {
        if (!SaveCustomerRequestValidator.ParseIdentificationType(request.IdentificationType))
        {
            throw new DomainException("customer_identification_type_invalid", "Seleccione un tipo de identificación válido.");
        }
        var type = Enum.Parse<IdentificationType>(request.IdentificationType.Trim(), ignoreCase: true);
        return new CustomerData(type, request.IdentificationNumber, request.Name, request.Email, request.Phone, request.Address);
    }

    private static CustomerResponse ToResponse(Customer customer)
    {
        return new CustomerResponse(
            customer.Id,
            customer.IdentificationType.ToString(),
            customer.IdentificationNumber,
            customer.Name,
            customer.Email,
            customer.Phone,
            customer.Address,
            customer.IsActive,
            IdentificationWarning(customer));
    }

    private static string? IdentificationWarning(Customer customer)
    {
        var result = customer.IdentificationType switch
        {
            IdentificationType.Cedula => EcuadorIdValidator.ValidateCedula(customer.IdentificationNumber),
            IdentificationType.Ruc => EcuadorIdValidator.ValidateRuc(customer.IdentificationNumber),
            _ => null,
        };
        if (result is null)
        {
            return null;
        }
        return result.Warnings.Concat(result.Errors).FirstOrDefault();
    }
}
