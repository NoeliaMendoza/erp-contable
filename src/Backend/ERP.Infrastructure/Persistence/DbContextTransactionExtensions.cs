using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    /// <summary>
    /// Ejecuta la operación en una transacción raíz y la publica para que otros módulos enlisten sus contextos.
    /// Solo el DbContext raíz confirma o revierte la transacción.
    /// </summary>
    public static async Task<T> InSharedTenantTransactionAsync<T>(
        this DbContext db,
        SharedTenantTransactionContext shared,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (shared.IsActive)
            return await db.InTenantTransactionAsync(shared, operation, cancellationToken);

        await using var rootTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
        shared.Start(db.Database.GetDbConnection(), rootTransaction.GetDbTransaction());
        try
        {
            var result = await operation(cancellationToken);
            await rootTransaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            shared.Clear();
        }
    }

    /// <summary>
    /// Reutiliza la transacción compartida si un caso de uso raíz la publicó; de lo contrario conserva el
    /// comportamiento habitual de una transacción local del contexto.
    /// </summary>
    public static async Task<T> InTenantTransactionAsync<T>(
        this DbContext db,
        SharedTenantTransactionContext shared,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
            return await operation(cancellationToken);

        if (!shared.IsActive)
            return await db.InTenantTransactionAsync(operation, cancellationToken);

        var connection = shared.Connection!;
        var replaceConnection = !ReferenceEquals(db.Database.GetDbConnection(), connection);
        var connectionString = db.Database.GetConnectionString();
        if (replaceConnection)
            db.Database.SetDbConnection(connection);

        try
        {
            await using var enlisted = await db.Database.UseTransactionAsync(shared.Transaction!, cancellationToken)
                ?? throw new InvalidOperationException("No se pudo enlistar el DbContext en la transacción compartida.");
            return await operation(cancellationToken);
        }
        finally
        {
            if (replaceConnection)
            {
                db.Database.SetDbConnection(null);
                db.Database.SetConnectionString(connectionString);
            }
        }
    }
}
