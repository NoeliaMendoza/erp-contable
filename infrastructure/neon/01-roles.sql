-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- Roles de mínimo privilegio para Neon. Ejecutar UNA sola vez, en la rama `development`,
-- desde el SQL Editor de Neon conectado con el rol dueño (p. ej. neondb_owner) a la base `neondb`.
-- Las ramas personales (dev-<nombre>) creadas DESPUÉS heredan estos roles.
--
-- ANTES de ejecutar: reemplace los dos <CAMBIAR_...> por contraseñas largas y aleatorias
-- (Neon exige contraseñas con suficiente entropía; use 32+ caracteres generados, sin comillas simples).
-- NO guarde este archivo con las contraseñas reales ni lo suba a GitHub.
--
-- Equivalente local: infrastructure/postgres/init/01-roles.sh
-- ─────────────────────────────────────────────────────────────────────────────────────────────

-- Grupo al que las migraciones otorgan permisos de lectura/escritura (nunca DDL).
CREATE ROLE erp_runtime NOLOGIN;

-- Aplica migraciones (DbMigrator). Será dueño de los esquemas que cree. Sin BYPASSRLS.
CREATE ROLE erp_migrator LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE
    PASSWORD '<CAMBIAR_PASSWORD_MIGRATOR>';

-- Conexión de la API. Solo lectura/escritura de datos, sin DDL y sin BYPASSRLS.
CREATE ROLE erp_app LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE
    PASSWORD '<CAMBIAR_PASSWORD_APP>';

GRANT erp_runtime TO erp_app;

-- Permisos sobre la base (cambie neondb si usan otra base).
GRANT CONNECT, CREATE, TEMPORARY ON DATABASE neondb TO erp_migrator;
GRANT CONNECT, TEMPORARY ON DATABASE neondb TO erp_runtime;

-- Verificación: ambas columnas deben ser false.
SELECT rolname, rolsuper, rolbypassrls
FROM pg_roles
WHERE rolname IN ('erp_runtime', 'erp_migrator', 'erp_app')
ORDER BY rolname;
