using System.Data.Common;
using ERP.BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Al iniciar cada transacción, transmite el tenant y el usuario a PostgreSQL con
/// <c>set_config(..., is_local =&gt; true)</c>. El valor vive solo dentro de la transacción, por lo que
/// no se filtra entre solicitudes aunque la conexión física se reutilice desde el pool.
/// Fuera de una transacción no hay contexto y RLS devuelve cero filas (denegación por defecto).
/// </summary>
public sealed class TenantSessionInterceptor(ITenantContext tenantContext) : DbTransactionInterceptor
{
    internal const string Sql =
        "SELECT set_config('app.tenant_id', @tenant_id, true), set_config('app.user_id', @user_id, true)";

    public override DbTransaction TransactionStarted(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        using var command = CreateCommand(connection, result);
        command.ExecuteNonQuery();
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection, result);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    private DbCommand CreateCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Sql;
        AddParameter(command, "tenant_id", tenantContext.TenantId?.ToString() ?? string.Empty);
        AddParameter(command, "user_id", tenantContext.UserId?.ToString() ?? string.Empty);
        return command;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
