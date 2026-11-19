using ERP.BuildingBlocks.Domain;
using ERP.BuildingBlocks.Paging;
using ERP.BuildingBlocks.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Modules.Accounting.Data;
using ERP.Modules.Accounting.Dtos;
using ERP.Modules.Accounting.Models;
using ERP.Modules.Accounting.Validators;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Modules.Accounting.Services;

/// <summary>CRUD del plan. Todas las consultas y escrituras usan el contexto RLS transaccional.</summary>
public sealed class AccountService(AccountingDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public Task<PagedResult<AccountResponse>> ListAsync(PageRequest page, bool? isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var query = db.Accounts.AsNoTracking();
            if (isActive is not null)
            {
                query = query.Where(a => a.IsActive == isActive);
            }

            if (page.Term is { } term)
            {
                var pattern = "%" + term.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
                query = query.Where(a => EF.Functions.ILike(a.Code, pattern, "\\") || EF.Functions.ILike(a.Name, pattern, "\\"));
            }

            var count = await query.CountAsync(ct);
            var items = await query.OrderBy(a => a.Code).ThenBy(a => a.Id).Skip(page.Skip).Take(page.SafePageSize)
                .Select(a => new AccountResponse(a.Id, a.Code, a.Name, a.Kind.ToString(), a.NormalBalance.ToString(), a.ParentId, a.AllowsPosting, a.IsActive))
                .ToListAsync(ct);
            return new PagedResult<AccountResponse>(items, count, page.SafePage, page.SafePageSize);
        }, cancellationToken);

    public Task<AccountResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct => ToResponse(await FindAsync(id, ct)), cancellationToken);

    public Task<AccountResponse> CreateAsync(SaveAccountRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            await LockTreeAsync(ct);
            var account = Account.Create(Guid.CreateVersion7(), tenant.RequireTenantId(), Data(request), clock.GetUtcNow(), tenant.UserId);
            await EnsureParentAsync(account.Id, account.ParentId, ct);
            await EnsureCodeAsync(account.Code, null, ct);
            db.Accounts.Add(account);
            await SaveAsync(account.Code, ct);
            return ToResponse(account);
        }, cancellationToken);

    public Task<AccountResponse> UpdateAsync(Guid id, SaveAccountRequest request, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            await LockTreeAsync(ct);
            var account = await FindAsync(id, ct);
            var data = Data(request);
            await EnsureParentAsync(id, data.ParentId, ct);
            await EnsureCodeAsync(Account.NormalizeCode(data.Code), id, ct);
            account.Update(data, clock.GetUtcNow(), tenant.UserId);
            await SaveAsync(account.Code, ct);
            return ToResponse(account);
        }, cancellationToken);

    public Task<AccountResponse> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
        db.InTenantTransactionAsync(async ct =>
        {
            var account = await FindAsync(id, ct);
            account.SetActive(isActive, clock.GetUtcNow(), tenant.UserId);
            await db.SaveChangesAsync(ct);
            return ToResponse(account);
        }, cancellationToken);

    // Un bloqueo transaccional por empresa evita ciclos por cambios simultáneos A->B y B->A.
    // Se obtiene ANTES de leer el árbol (ReadCommitted, configuración estándar de PostgreSQL).
    private Task<int> LockTreeAsync(CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({tenant.RequireTenantId().ToString()}, 0))", ct);

    private async Task EnsureParentAsync(Guid accountId, Guid? parentId, CancellationToken ct)
    {
        var visited = new HashSet<Guid> { accountId };
        while (parentId is { } id)
        {
            if (!visited.Add(id))
            {
                throw new DomainException("account_hierarchy_cycle", "La cuenta padre produciría un ciclo en el plan de cuentas.");
            }

            var parent = await db.Accounts.AsNoTracking().Where(a => a.Id == id)
                .Select(a => new { a.ParentId }).SingleOrDefaultAsync(ct)
                ?? throw new DomainException("account_parent_not_available", "La cuenta padre no existe en la empresa activa.");
            parentId = parent.ParentId;
        }
    }

    private async Task<Account> FindAsync(Guid id, CancellationToken ct) =>
        await db.Accounts.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Cuenta", id);

    private async Task EnsureCodeAsync(string code, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Accounts.AnyAsync(a => a.Code == code && a.Id != exceptId, ct))
        {
            throw Duplicate(code);
        }
    }

    private async Task SaveAsync(string code, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_accounts_tenant_id_code",
        })
        {
            throw Duplicate(code);
        }
    }

    private static ConflictException Duplicate(string code) =>
        new("account_code_duplicated", $"Ya existe una cuenta con el código '{code}' en esta empresa.");

    private static AccountData Data(SaveAccountRequest request)
    {
        if (!AccountValues.TryParse<AccountKind>(request.Kind, out var kind)
            || !AccountValues.TryParse<NormalBalance>(request.NormalBalance, out var balance))
        {
            throw new DomainException("account_type_invalid", "El tipo y la naturaleza de la cuenta deben ser válidos.");
        }

        return new(request.Code, request.Name, kind, balance, request.ParentId, request.AllowsPosting);
    }

    private static AccountResponse ToResponse(Account a) =>
        new(a.Id, a.Code, a.Name, a.Kind.ToString(), a.NormalBalance.ToString(), a.ParentId, a.AllowsPosting, a.IsActive);
}
