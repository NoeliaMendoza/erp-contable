# Arquitectura

## Flujo

```text
Avalonia (MVVM) ──HTTPS/JSON /api/v1──▶ ASP.NET Core
                                         ├─ Autenticación (dev: X-Dev-User · pendiente: OpenIddict)
                                         ├─ TenantResolutionMiddleware (X-Tenant-Id = selección, validada contra membresía)
                                         ├─ Autorización por permiso (políticas perm:<código>)
                                         └─ Módulos (casos de uso) ──EF Core/Npgsql──▶ PostgreSQL (RLS FORCE, rol erp_app)
                                                              └─ Redis (caché auxiliar, claves por tenant)
```

El escritorio nunca recibe credenciales de base de datos. PostgreSQL es la fuente de verdad; Redis puede caer sin afectar la integridad.

## Monolito modular

- Un solo proceso y una sola base de datos; **un esquema PostgreSQL y un DbContext por módulo** (`platform`, `accounting`, …), cada uno con su historial de migraciones (`<esquema>.__ef_migrations_history`).
- **Patrón MVC** dentro de cada módulo (carpetas, no proyectos):
  - `Models/` — entidades y reglas de negocio (la "M").
  - `Controllers/` — controladores HTTP que heredan de `ApiControllerBase` (la "C"); solo transporte, validación y permisos.
  - `Services/` — casos de uso que coordinan Models y Data.
  - `Dtos/` — objetos de entrada/salida de la API (nunca se devuelven entidades).
  - `Validators/` — FluentValidation.
  - `Data/` — DbContext, configuración EF y `Migrations/`.
  - La "V" (vista) es el front: hoy el cliente Avalonia (MVVM) en `src/Frontend`.
- Comunicación entre módulos **solo** mediante proyectos `*.Contracts` (interfaces + DTO). Verificado por `tests/ERP.ArchitectureTests`.
- `ERP.Api` y `ERP.DbMigrator` son los únicos que componen todos los módulos.

## Mapa de módulos y dependencias (sin ciclos)

```text
BuildingBlocks ◀── Infrastructure ◀── Modules.* ◀── Api, DbMigrator
      ▲                                  │
      ├── Tributario ◀──────────────────────┤ (Platform, Sales, Purchases)
      ├── Accounting.Contracts ◀─────────┤ (Accounting implementa; Sales, Purchases, Inventory consumen)
      └── Inventory.Contracts ◀──────────┘ (Inventory implementa; Sales, Purchases consumen)

Frontend: ERP.Frontend.Shared ◀── ERP.Frontend   (sin referencias al backend)
```

| Módulo | Responsable (provisional) | Expone | Consume |
|---|---|---|---|
| Platform | Integrante 1 | `ITenantContext` (vía Infrastructure), `ITenantAccessResolver`, `/api/v1/me`, `/api/v1/tenant/*` | Tributario (validación RUC) |
| Accounting | Integrante 2 | `IAccountingPostingService` | — |
| Sales + SRI | Integrante 3 | (por definir: `ISalesQueries` para Reporting) | Accounting.Contracts, Inventory.Contracts, Tributario |
| Purchases/Treasury | Integrante 4 | (por definir) | Accounting.Contracts, Inventory.Contracts, Tributario |
| Inventory | Integrante 5 | `IProductCatalog`, `IStockQueryService`, `IInventoryMovementService` | Accounting.Contracts |
| Reporting | Integrante 6 | Dashboard, exportaciones | Consultas de lectura publicadas por otros módulos |

## Convenciones de API

- Rutas `/api/v1/...`; cambios incompatibles → `/api/v2`.
- DTO separados de entidades; errores como `ProblemDetails` con extensión `code` estable (422 reglas de negocio, 404 recurso inexistente o ajeno, 400 validación, 403 permiso/membresía).
- Cada acción declara `[RequirePermission(Permisos.X)]` (`ERP.Infrastructure.Web`); una prueba (`ControllerConventionsTests`) falla si falta.
- Lecturas tenant-scoped siempre dentro de `db.InTenantTransactionAsync(...)`.
- `CancellationToken` en toda E/S; `TimeProvider` para la hora; instantes en UTC (`timestamptz`), fechas fiscales como `DateOnly`.

## Escritorio

- `ERP.Frontend.Shared`: `ErpApiClient` (único punto de acceso HTTP), `SessionHeadersHandler`, `SessionState`, `ViewModelBase` (carga/errores), tema `ErpTheme.axaml`.
- `ERP.Frontend`: `MainWindowViewModel` (login → empresa → shell), `ShellViewModel` (menú por módulos habilitados), `ViewLocator` explícito, carpetas `Modules/<Modulo>`.
- Paginación/filtrado se harán en servidor; no se descargan libros completos al cliente. Sin modo offline (extensión futura documentada).
