# Seguridad multi-tenant

Tres capas independientes; ninguna sustituye a las otras.

## 1. API (autorización)

1. La identidad autenticada da el usuario (`sub`).
2. `X-Tenant-Id` es solo una **selección** del cliente. `TenantResolutionMiddleware` fija un tenant *candidato*, consulta la membresía activa y el tenant activo, y carga los permisos efectivos. Sin membresía → **403** (sin revelar si la empresa existe).
3. Cada acción de controlador declara `[RequirePermission(código)]`; `PermissionAuthorizationHandler` comprueba el permiso en el tenant confirmado. Ocultar botones en Avalonia es solo UX.

## 2. EF Core

- `TenantAwareDbContext` aplica filtro global `TenantId == tenant actual` a toda entidad `ITenantOwned` (sin tenant → ninguna fila).
- Al guardar, asigna el tenant a entidades nuevas y **bloquea** escrituras de otro tenant.
- `AutoTransactionBehavior.Always`: toda escritura ocurre en transacción (necesario para el contexto RLS).

## 3. PostgreSQL (RLS)

- `TenantSessionInterceptor` ejecuta al iniciar cada transacción
  `set_config('app.tenant_id', @t, true), set_config('app.user_id', @u, true)` — **local a la transacción**, parametrizado, seguro con pooling.
- Funciones `app_security.current_tenant_id()` / `current_user_id()`; políticas con `USING` y `WITH CHECK`; `FORCE ROW LEVEL SECURITY` (el dueño también queda sujeto).
- **Denegación por defecto**: sin contexto, cero filas.

### Roles

| Rol | Uso | Privilegios |
|---|---|---|
| `erp_runtime` (NOLOGIN) | Grupo al que las migraciones otorgan DML | Sin DDL |
| `erp_app` | Conexión de la API | Miembro de `erp_runtime`; NOSUPERUSER, **NOBYPASSRLS** |
| `erp_migrator` | DbMigrator / CI | Dueño de la base/esquemas; NOSUPERUSER, NOBYPASSRLS |

## Pruebas negativas existentes (`tests/ERP.IntegrationTests`)

- Rol runtime no es superusuario ni BYPASSRLS; no puede desactivar RLS.
- Sin contexto no se ve ninguna fila (6 tablas); con contexto A solo filas de A.
- Insertar con `tenant_id` de B estando en A → `42501`; UPDATE/DELETE sobre B afectan 0 filas.
- El contexto no sobrevive al commit (seguridad con pool).
- API: 401 sin identidad; 403 al seleccionar empresa ajena; 404 por id adivinado de otra empresa; 403 al escribir con rol de consulta; listados y configuración fiscal aislados; 400 sin empresa.

## Pendientes

- OIDC (Identity + OpenIddict, Authorization Code + PKCE, navegador del sistema; tokens en almacén protegido del SO) — PLAT-01.
- Auditoría completa (actor, acción, referencia, cambios) — PLAT-05.
- Tabla `users` sin RLS (lectura global por el runtime): revisar al integrar Identity.
- Operaciones globales de plataforma (alta de tenants) requieren un rol/endpoint explícito; hoy solo vía semillas.
- Pruebas de aislamiento en exportaciones, archivos y reportes cuando existan.
