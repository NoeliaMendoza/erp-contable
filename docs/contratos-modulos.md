# Contratos entre módulos (v1)

Regla: un módulo **nunca** lee ni escribe tablas de otro. Usa interfaces de un proyecto `*.Contracts`.
Cambiar un contrato = PR revisado por productor **y** consumidores.

## Platform → todos
- `ITenantContext` (`ERP.BuildingBlocks.Tenancy`): `UserId`, `TenantId`, `Permissions`, `RequireTenantId()`.
- Permisos: cada módulo publica `IPermissionProvider` y constantes `XPermissions`. Códigos `modulo.recurso.accion`.
- Feature codes: `platform`, `accounting`, `sales`, `purchases`, `inventory`, `reporting`.

## Accounting.Contracts — `IAccountingPostingService` (implementación pendiente, Integrante 2)
- `PostAsync(PostingRequest)`: idempotente por `IdempotencyKey` (`"<modulo>:<tipo>:<id>"`), valida partida doble, período abierto y cuentas del tenant; devuelve el asiento existente si la clave ya se procesó.
- `ReverseAsync(...)`: reverso auditable; nunca se borra un asiento contabilizado.
- Las cuentas se referencian por **código** del plan del tenant; la tabla de mapeo (p. ej. "cuenta de IVA ventas") la define Contabilidad con Ventas/Compras (ACC-06).
- Transacción: se ejecuta dentro de la transacción del caso de uso llamador (misma base). Si en el futuro se separan bases → documentar outbox.

## Inventory.Contracts (implementación pendiente, Integrante 5)
- `IProductCatalog.GetByIdsAsync`, `IStockQueryService.GetStockAsync`.
- `IInventoryMovementService.RegisterAsync(InventoryMovementRequest)`: idempotente, con control de concurrencia; política de stock negativo y costeo → D-06.

## Tributario (`ERP.Tributario`)
- `EcuadorIdValidator` (sintáctico), `TaxRateCatalog` / `ITaxRateCatalogProvider` (tarifas por vigencia, nunca constantes), contratos SRI (`ISriGateway`, `IElectronicDocumentSigner`, `IAccessKeyGenerator`, `IElectronicDocumentXmlBuilder`).

## Reporting ← módulos
- Cada módulo publica consultas de lectura (proyecciones) en su `*.Contracts` cuando Reporting las necesite. Prohibido el acceso directo a tablas ajenas.

## API pública v1 (escritorio)
| Método | Ruta | Permiso |
|---|---|---|
| GET | `/api/v1/me` | autenticado |
| GET | `/api/v1/tenant/current` | `platform.tenant.read` |
| GET | `/api/v1/tenant/branches` · `/{id}` | `platform.branches.read` |
| POST | `/api/v1/tenant/branches` | `platform.branches.manage` |
| GET | `/api/v1/tenant/taxpayers` | `platform.taxpayers.read` |

Los modelos del escritorio (`ERP.Frontend.Shared/Api/ApiModels.cs`) se mantienen sincronizados manualmente con estos DTO.
