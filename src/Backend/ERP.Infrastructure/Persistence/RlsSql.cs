namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Fragmentos SQL estándar para migraciones. Cada módulo los usa en sus migraciones EF
/// (<c>migrationBuilder.Sql(...)</c>) para que todas las tablas tenant-scoped tengan la misma política.
/// </summary>
public static class RlsSql
{
    /// <summary>Rol de grupo (NOLOGIN) que reciben los usuarios de conexión de la API. Nunca superusuario ni BYPASSRLS.</summary>
    public const string RuntimeRole = "erp_runtime";

    public const string SecuritySchema = "app_security";

    /// <summary>Funciones que leen el contexto local a la transacción. Se crean en la migración inicial de Platform.</summary>
    public static string CreateSecurityFunctions() => $"""
        CREATE SCHEMA IF NOT EXISTS {SecuritySchema};

        CREATE OR REPLACE FUNCTION {SecuritySchema}.current_tenant_id() RETURNS uuid
            LANGUAGE sql STABLE
            AS $$ SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid $$;

        CREATE OR REPLACE FUNCTION {SecuritySchema}.current_user_id() RETURNS uuid
            LANGUAGE sql STABLE
            AS $$ SELECT NULLIF(current_setting('app.user_id', true), '')::uuid $$;

        GRANT USAGE ON SCHEMA {SecuritySchema} TO {RuntimeRole};
        GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA {SecuritySchema} TO {RuntimeRole};
        """;

    /// <summary>
    /// Habilita y fuerza RLS en una tabla con columna <c>tenant_id</c>, con denegación por defecto:
    /// solo se ven y escriben filas del tenant del contexto. Otorga CRUD al rol runtime.
    /// </summary>
    public static string EnableTenantIsolation(string schema, string table, string tenantColumn = "tenant_id") => $"""
        ALTER TABLE {schema}.{table} ENABLE ROW LEVEL SECURITY;
        ALTER TABLE {schema}.{table} FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON {schema}.{table}
            USING ({tenantColumn} = {SecuritySchema}.current_tenant_id())
            WITH CHECK ({tenantColumn} = {SecuritySchema}.current_tenant_id());
        GRANT SELECT, INSERT, UPDATE, DELETE ON {schema}.{table} TO {RuntimeRole};
        """;

    /// <summary>Concede uso del esquema del módulo al rol runtime (sin privilegios DDL).</summary>
    public static string GrantSchemaUsage(string schema) =>
        $"GRANT USAGE ON SCHEMA {schema} TO {RuntimeRole};";
}
