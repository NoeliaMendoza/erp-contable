using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence;

public static class DbContextTransactionExtensions
{
    /// <summary>
    /// Ejecuta una operación dentro de una transacción (necesaria para que PostgreSQL reciba el contexto
    /// de tenant). Úsese en toda lectura de datos tenant-scoped. Hace commit solo si no hubo excepción.
    /// Si ya existe una transacción activa, la reutiliza.
    /// </summary>
    public static async Task<T> InTenantTransactionAsync<T>(
        this DbContext db, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
