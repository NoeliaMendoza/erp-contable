# Conexión a Neon (integración) — procedimiento, sin credenciales

> No se ha conectado nada a Neon desde esta base. Verificar en la consola los nombres vigentes de rama
> (`producción` / `prueba`) y base (`neondb` / `erp_db`) antes de automatizar. Ramas de Neon ≠ ramas de GitHub.

1. **Usar solo la rama de prueba** para integración. La rama estable/demo no recibe desarrollos inacabados.
2. Crear (consola SQL de Neon, con el rol propietario, una sola vez) los roles con mínimo privilegio, equivalentes a `infrastructure/postgres/init/01-roles.sh`:
   ```sql
   CREATE ROLE erp_runtime NOLOGIN;
   CREATE ROLE erp_migrator LOGIN PASSWORD '<generada>';
   CREATE ROLE erp_app LOGIN PASSWORD '<generada>' IN ROLE erp_runtime;
   GRANT CREATE ON DATABASE <base> TO erp_migrator;   -- o hacerlo dueño de la base
   GRANT CONNECT ON DATABASE <base> TO erp_runtime;
   ```
   Verificar que ni `erp_app` ni `erp_migrator` tengan `BYPASSRLS` (`SELECT rolname, rolbypassrls FROM pg_roles`).
3. Guardar cadenas en **User Secrets** (individual) o **secretos de GitHub Actions**; nunca en `appsettings*.json`:
   `Host=<endpoint>.neon.tech;Database=<base>;Username=erp_app;Password=...;SSL Mode=VerifyFull;Channel Binding=Require`
   (usar el *pooler* de Neon para la API; conexión directa para migraciones).
4. Respaldo antes de migrar: crear una rama Neon de respaldo desde la rama de prueba.
5. Aplicar migraciones (responsable de integración): `dotnet run --project src/Backend/ERP.DbMigrator -- migrate` con `ConnectionStrings__ErpMigrator` de Neon. **No** ejecutar `seed-dev` en la rama estable.
6. Ejecutar pruebas de humo de aislamiento contra la rama de prueba.
7. Vigilar CU-horas y almacenamiento; no dejar conexiones inactivas.
