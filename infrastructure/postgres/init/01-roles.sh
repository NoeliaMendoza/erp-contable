#!/bin/sh
# Se ejecuta solo la primera vez que se crea el volumen de PostgreSQL local.
# Crea los roles con mínimo privilegio y la base del ERP. Equivalente documentado para Neon en docs/neon.md.
#   erp_runtime  : rol de grupo NOLOGIN al que las migraciones otorgan permisos.
#   erp_migrator : dueño de la base y de los esquemas; aplica migraciones. Sin SUPERUSER ni BYPASSRLS.
#   erp_app      : usuario de conexión de la API. Miembro de erp_runtime. Sin DDL, sin BYPASSRLS.
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v migrator_pw="$ERP_MIGRATOR_PASSWORD" -v app_pw="$ERP_APP_PASSWORD" -v dbname="$ERP_DB_NAME" <<'SQL'
CREATE ROLE erp_runtime NOLOGIN;
CREATE ROLE erp_migrator LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB PASSWORD :'migrator_pw';
CREATE ROLE erp_app LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB IN ROLE erp_runtime PASSWORD :'app_pw';
CREATE DATABASE :"dbname" OWNER erp_migrator;
REVOKE ALL ON DATABASE :"dbname" FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE :"dbname" TO erp_runtime;
SQL

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$ERP_DB_NAME" <<'SQL'
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
SQL
