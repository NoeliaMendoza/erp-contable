using ERP.Infrastructure.Persistence;

namespace ERP.Modules.Accounting.Data;

/// <summary>
/// Política estándar para accounting.accounts. InitialAccounting instala RLS y permisos en la migración.
/// Estos métodos solo devuelven SQL y no abren conexiones ni modifican la base de datos.
/// </summary>
public static class AccountingDatabaseSecurity
{
    /// <summary>Tablas de asientos protegidas contra modificación y borrado.</summary>
    public static readonly string[] ImmutableTables = ["journal_entries", "journal_entry_lines"];

    public static string TenantIsolationSql() =>
        RlsSql.GrantSchemaUsage(AccountingDbContext.Schema) + Environment.NewLine
        + RlsSql.EnableTenantIsolation(AccountingDbContext.Schema, "accounts");

    /// <summary>
    /// Asientos inmutables (P1-5). <c>EnableTenantIsolation</c> concede el CRUD completo porque es la
    /// política común de las tablas por empresa; aquí se retira UPDATE y DELETE al rol de la aplicación,
    /// de modo que un error de código no pueda alterar ni borrar un asiento ya contabilizado.
    /// Las correcciones se registran como un reverso. El rol de migraciones conserva el DDL.
    /// </summary>
    public static string PostingImmutabilitySql() =>
        string.Join(Environment.NewLine, ImmutableTables.Select(table =>
            $"REVOKE UPDATE, DELETE ON {AccountingDbContext.Schema}.{table} FROM {RlsSql.RuntimeRole};"));
}
