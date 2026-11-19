# Contratos entre módulos (v1)

Regla: un módulo **nunca** lee ni escribe tablas de otro. Usa interfaces de un proyecto `*.Contracts`.
Cambiar un contrato = PR revisado por productor **y** consumidores.

## Platform → todos
- `ITenantContext` (`ERP.BuildingBlocks.Tenancy`): `UserId`, `TenantId`, `Permissions`, `RequireTenantId()`.
- Permisos: cada módulo publica `IPermissionProvider` y constantes `XPermissions`. Códigos `modulo.recurso.accion`.
- Feature codes: `platform`, `accounting`, `sales`, `purchases`, `inventory`, `reporting`.

## Sesión del escritorio (Platform → Frontend, PLAT-01 · Reyes)

Interfaces en `ERP.Frontend.Shared/Session/AuthenticationContracts.cs`. Las pantallas y el cliente HTTP dependen
**solo** de ellas; nadie usa OpenIddict ni guarda tokens por su cuenta.

| Tipo | Uso |
|---|---|
| `IAuthenticationService` | `SignInAsync(ct)` abre el navegador del sistema (Authorization Code + PKCE) y devuelve `SignInResult` (`Succeeded` / `Cancelled` / `Failed` + mensaje para el usuario). `SignOutAsync(ct)` cierra la sesión local. Evento `SessionEnded` (`SignedOut` / `Expired`). |
| `IAccessTokenProvider` | Lo consume `SessionHeadersHandler`: `GetAccessTokenAsync(ct)` devuelve el token vigente (lo renueva una vez si está por expirar) o `null`; `ReportUnauthorized(token)` cuando la API responde 401 → fin de sesión, **sin reintentos**. |
| `SessionState` | Se mantiene: perfil (`/api/v1/me`), empresa seleccionada, permisos y módulos habilitados (solo UX). |

Reglas:
- La API autentica con `Authorization: Bearer <token>`; `X-Tenant-Id` es solo la **selección** de empresa y el servidor
  valida membresía y permiso en cada solicitud (sin membresía → 403).
- Tokens solo en memoria; nunca en registros, archivos, capturas ni Git.
- `X-Dev-User` queda solo para Development/Testing (pruebas y `curl`); el escritorio deja de enviarlo.
- Un módulo nuevo **no** necesita tocar nada de esto: usa `ErpApiClient.GetAsync<T>` / `SendJsonAsync<T>`, que ya
  adjuntan token y empresa.

## Plan de cuentas (ACC-01, Romero)

El CRUD de `/api/v1/accounts` usa los permisos `accounting.accounts.read` y `accounting.accounts.write`.
Empresa y auditoría se obtienen del servidor. El código es texto normalizado y único por empresa,
incluso si la cuenta está inactiva; el padre pertenece a la misma empresa y no puede crear ciclos.
La desactivación conserva las relaciones y no desactiva hijos en cascada.
DTO, errores, migración y demostración: [modulos/contabilidad.md](modulos/contabilidad.md).
Este mantenimiento no implementa todavía el servicio de contabilización del siguiente apartado.

## Accounting.Contracts — `IAccountingPostingService` (implementación pendiente, Integrante 2)
- `PostAsync(PostingRequest)`: idempotente por `IdempotencyKey` (`"<modulo>:<tipo>:<id>"`), valida partida doble, período abierto y cuentas del tenant; devuelve el asiento existente si la clave ya se procesó.
- `ReverseAsync(...)`: reverso auditable; nunca se borra un asiento contabilizado.
- Las cuentas se referencian por **código** del plan del tenant; la tabla de mapeo (p. ej. "cuenta de IVA ventas") la define Contabilidad con Ventas/Compras (ACC-06).
- Transacción: se ejecuta dentro de la transacción del caso de uso llamador (misma base). Si en el futuro se separan bases → documentar outbox.

## Inventory.Contracts (Mendoza)
- `IProductCatalog.GetByIdsAsync`: **implementado** (INV-01). Solo artículos de la empresa activa; incluye `VatTreatment` (Ventas resuelve el código SRI y el porcentaje vigente con `TaxRateCatalog`), `TracksInventory` y `Barcode`; detalle en [modulos/inventario.md](modulos/inventario.md).
- `IStockQueryService.GetStockAsync`: pendiente (INV-03).
- `IInventoryMovementService.RegisterAsync(InventoryMovementRequest)`: idempotente, con control de concurrencia; política de stock negativo y costeo → D-06.

## Tributario (`ERP.Tributario`)
- `EcuadorIdValidator` (sintáctico), `TaxRateCatalog` / `ITaxRateCatalogProvider` (tarifas por vigencia, nunca constantes), contratos SRI (`ISriGateway`, `IElectronicDocumentSigner`, `IAccessKeyGenerator`, `IElectronicDocumentXmlBuilder`).

## Reporting ← módulos
- Cada módulo publica consultas de lectura (proyecciones) en su `*.Contracts` cuando Reporting las necesite. Prohibido el acceso directo a tablas ajenas.

## Convenciones CRUD comunes (v1)

Todo módulo nuevo sigue este patrón (implementado primero en Inventario):

- Rutas: `GET /api/v1/{recurso}` (lista), `GET /{id}`, `POST`, `PUT /{id}`, `PATCH /{id}/status` `{ isActive }`. Sin borrado físico: baja lógica.
- Paginación: `page` desde 1, `pageSize` 20 por defecto y máximo 100 (valores fuera de rango se ajustan), `search` opcional. Respuesta `PagedResult<T>` (`ERP.BuildingBlocks.Paging`): `items`, `totalCount`, `page`, `pageSize`, orden estable.
- Permisos: `modulo.recurso.read` y `modulo.recurso.write`.
- Errores (`ProblemDetails` con `code` y `traceId`): 400 validación con `errors` por campo · 401 sin sesión · 403 sin permiso o empresa ajena · 404 no existe en la empresa activa · **409 duplicado o conflicto** (`ConflictException`) · 422 otra regla de negocio (`DomainException`).
- Códigos de texto (SKU, código de unidad…): recortar espacios y MAYÚSCULAS; únicos por empresa.
- Escritorio: cada módulo crea su cliente (p. ej. `InventoryApi`) sobre `ErpApiClient.GetAsync<T>` / `SendJsonAsync<T>` sin editar el cliente común.

## API pública v1 (escritorio)
| Método | Ruta | Permiso |
|---|---|---|
| GET | `/api/v1/me` | autenticado |
| GET/POST/PUT/PATCH | `/api/v1/accounts` · `/{id}` · `/{id}/status` | `accounting.accounts.read` / `.write` |
| GET | `/api/v1/tenant/current` | `platform.tenant.read` |
| GET | `/api/v1/tenant/branches` · `/{id}` | `platform.branches.read` |
| POST | `/api/v1/tenant/branches` | `platform.branches.manage` |
| GET/POST/PUT/PATCH | `/api/v1/units` · `/{id}` · `/{id}/status` | `inventory.units.read` / `.write` |
| GET/POST/PUT/PATCH | `/api/v1/categories` · `/{id}` · `/{id}/status` | `inventory.categories.read` / `.write` |
| GET/POST/PUT/PATCH | `/api/v1/products` · `/{id}` · `/{id}/status` | `inventory.products.read` / `.write` |
| GET | `/api/v1/tenant/taxpayers` | `platform.taxpayers.read` |

Los modelos del escritorio (`ERP.Frontend.Shared/Api/ApiModels.cs`) se mantienen sincronizados manualmente con estos DTO.
