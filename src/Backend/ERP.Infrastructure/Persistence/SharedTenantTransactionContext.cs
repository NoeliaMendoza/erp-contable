using System.Data.Common;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Transacción PostgreSQL compartida durante un caso de uso tenant-scoped. El contexto raíz conserva el control
/// del commit; los demás DbContext se enlistan en la misma conexión y transacción.
/// </summary>
public sealed class SharedTenantTransactionContext
{
    public DbConnection? Connection { get; private set; }
    public DbTransaction? Transaction { get; private set; }
    public bool IsActive => Connection is not null && Transaction is not null;

    internal void Start(DbConnection connection, DbTransaction transaction)
    {
        if (IsActive)
            throw new InvalidOperationException("Ya existe una transacción compartida en este ámbito.");
        Connection = connection;
        Transaction = transaction;
    }

    internal void Clear()
    {
        Connection = null;
        Transaction = null;
    }
}
