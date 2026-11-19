using ERP.Infrastructure.Persistence;

namespace ERP.Modules.Accounting.Data;

/// <summary>
/// Política estándar para accounting.accounts. InitialAccounting instala RLS y permisos en la migración.
/// Este método solo devuelve SQL y no abre conexiones ni modifica la base de datos.
/// </summary>
public static class AccountingDatabaseSecurity
{
    public static string TenantIsolationSql() =>
        RlsSql.GrantSchemaUsage(AccountingDbContext.Schema) + Environment.NewLine
        + RlsSql.EnableTenantIsolation(AccountingDbContext.Schema, "accounts");
}
