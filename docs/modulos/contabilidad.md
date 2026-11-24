# Contabilidad: ACC-01 - Plan de cuentas

Responsable: Romero. Issue [#6](https://github.com/NoeliaMendoza/erp-contable/issues/6).
Rama de trabajo: `feature/accounting-plan-cuentas`. Revisión local: 19 de noviembre de 2026.
La referencia de alcance son los criterios de GitHub.

## Alcance y estado

Implementa el plan de cuentas por empresa: API, dominio, persistencia PostgreSQL,
validaciones, permisos y pantalla Avalonia conectada al cliente HTTP común.
No incluye el servicio de contabilización, asientos persistidos, libro diario, saldos ni cierres.
Los issues #12 y #17 corresponden al siguiente incremento.

La rama incorpora `origin/main` hasta `8c3126c` (componentes compartidos de Andy),
conservando los cambios de Contabilidad. No se modifica ni publica `main`.
La integración y la aprobación de la entrega corresponden a Noe y al revisor del equipo.

La migración `20261118222603_InitialAccounting` ya está en esta rama y registrada en
`DatabaseMigrator`, después de Platform, autenticación e Inventario. No hay que aplicar
un parche adicional. No se accede a Neon ni se generan migraciones de otros módulos.
Antes de aplicar el cambio a una base compartida se coordina con Noe.

## Modelo y reglas

| Campo | Regla |
|---|---|
| Code | Texto ASCII de 1 a 40 caracteres, recortado y en mayúsculas. Letras, números, punto, guion y guion bajo. Conserva ceros iniciales |
| Name | Texto recortado de 1 a 160 caracteres |
| Kind | Asset, Liability, Equity, Income o Expense |
| NormalBalance | Debit o Credit; admite naturaleza distinta del tipo para cuentas correctoras |
| ParentId | Opcional, de la misma empresa, sin autorreferencias ni ciclos |
| AllowsPosting | Indica si admite movimientos; no registra asientos en ACC-01 |
| IsActive | Activa al crear; cambia exclusivamente mediante PATCH de estado |
| TenantId | Obtenido de la sesión validada por el servidor |
| Auditoría | CreatedAtUtc, CreatedBy, UpdatedAtUtc y UpdatedBy |

- El código es único por empresa, incluso después de desactivar. El mismo código está permitido en otra empresa.
- No hay endpoint DELETE. Desactivar conserva relaciones e historial; no desactiva hijos en cascada.
- Se permiten padres inactivos y se identifican en el selector. ACC-01 no impone restricciones adicionales de tipo o movimiento al padre.
- La comprobación recorre los ancestros. Un advisory lock transaccional por empresa impide ciclos por ediciones simultáneas A->B y B->A.
- La edición conserva empresa, creador y fecha de creación.

## API y permisos

| Método | Ruta | Permiso |
|---|---|---|
| GET | /api/v1/accounts | accounting.accounts.read |
| GET | /api/v1/accounts/{id} | accounting.accounts.read |
| POST | /api/v1/accounts | accounting.accounts.write |
| PUT | /api/v1/accounts/{id} | accounting.accounts.write |
| PATCH | /api/v1/accounts/{id}/status | accounting.accounts.write |

`accounting.accounts.manage` se conserva en el catálogo por compatibilidad; no sustituye `.write`.
GET admite `page`, `pageSize`, `search` e `isActive`. Página inicial 1, tamaño predeterminado 20,
máximo 100 y orden por código e ID. La búsqueda es literal, sin interpretar `%`, `_` o `\` como comodines.
Respuesta `PagedResult<AccountResponse>`: `items`, `totalCount`, `page`, `pageSize`.

POST y PUT:

```json
{
  "code": "01.01",
  "name": "Caja",
  "kind": "Asset",
  "normalBalance": "Debit",
  "parentId": null,
  "allowsPosting": true
}
```

PATCH: `{ "isActive": false }`. Si falta `isActive`, devuelve 400.
TenantId y auditoría no son campos editables del contrato.

Errores con ProblemDetails y traceId: 400 validación, 401 sin sesión, 403 sin permiso o membresía,
404 para un ID no visible en la empresa, 409 código duplicado y 422 padre ajeno o ciclo.
Un padre ajeno se rechaza sin revelar a qué empresa pertenece.

## Persistencia y aislamiento

`AccountingDbContext` deriva de `TenantAwareDbContext`. Las operaciones usan el contexto RLS
transaccional común y verifican la empresa también en las escrituras.

`InitialAccounting` crea:

- Esquema `accounting` y tabla `accounts` con auditoría.
- Índice único `(tenant_id, code)` y clave alternativa `(tenant_id, id)`.
- FK restrictiva `(tenant_id, parent_id) -> (tenant_id, id)`.
- CHECK de código, nombre, tipo, naturaleza y padre distinto del propio ID.
- Grants comunes y RLS habilitado y forzado mediante `RlsSql.EnableTenantIsolation`.

Las pruebas de integración usan PostgreSQL 17 temporal con Testcontainers: el migrador crea el esquema
y la API usa `erp_app` sin SUPERUSER ni BYPASSRLS. La ausencia de la tabla hace fallar la prueba;
no se oculta omitiendo pruebas y no se ejecuta DDL alternativo desde ellas.

## Escritorio

La pantalla usa `AccountingApi` sobre `ErpApiClient`, sesión y encabezados existentes.
La navegación está registrada en `PageFactory`, `ViewLocator` y `ShellViewModel`.
El menú exige el módulo `accounting` y permiso de lectura. Los formularios y acciones de escritura
exigen `.write` en pantalla; el servidor vuelve a autorizar cada petición.

Reutiliza `FormField`, `FeedbackBar`, `SearchBox`, `EmptyState`, `ConfirmationDialog` y
`PaginationBar` de Andy. Incluye validaciones por campo, mensajes de guardado/error, selección de padre,
confirmación de estado y desplazamiento horizontal para tablas en ventanas estrechas.
El selector carga todas las páginas y excluye la propia cuenta y sus descendientes.
Las acciones no envían otra operación ni cambian el formulario mientras hay una petición en curso.
Si falla una desactivación, se conservan la confirmación y el error para poder reintentar.

## Verificación reproducible

Desde la raíz del repositorio, con .NET 10 en PATH y Docker abierto:

```powershell
dotnet build ERP.slnx --no-restore
dotnet format ERP.slnx --verify-no-changes --no-restore --severity warn
dotnet test --project tests/ERP.UnitTests --no-build
dotnet test --project tests/ERP.ArchitectureTests --no-build
dotnet test --project tests/ERP.IntegrationTests --no-build
dotnet ef migrations has-pending-model-changes --project src/Backend/ERP.Modules.Accounting --startup-project src/Backend/ERP.DbMigrator --context AccountingDbContext --no-build
```

Las pruebas cubren duplicados 409, códigos iguales entre empresas, padres ajenos, ciclos y concurrencia,
GET/PUT/PATCH cruzados 404, CRUD y auditoría, validación 400, autenticación/permisos 401/403,
paginación, búsqueda literal, RLS por SQL directo y lógica del formulario.

Los resultados de la revisión local se registran en `docs/modulos/acc01-validacion.md`.

## Demostración del plan de cuentas

Usar únicamente Docker local o una rama personal autorizada. Para el entorno local ya configurado:

```powershell
dotnet run --project src/Backend/ERP.DbMigrator -- migrate seed-dev
dotnet run --project src/Backend/ERP.Api --launch-profile https
dotnet run --project src/Frontend/ERP.Frontend
```

1. Iniciar sesión con una cuenta demo local. Seleccionar Comercial Andina Demo.
2. Abrir Plan de cuentas y crear `01` Activos, sin padre, con movimiento deshabilitado.
3. Crear `01.01` Caja con padre Activos, tipo Activo, naturaleza Deudora y movimiento habilitado.
4. Buscar, editar el nombre y comprobar que se conserva la jerarquía.
5. Cancelar una desactivación y comprobar que no cambia nada. Después confirmarla y mostrar Incluir inactivas.
6. Intentar reutilizar el código: debe devolver conflicto. Reactivar la cuenta original.
7. Cambiar de empresa: el listado es independiente y permite repetir el mismo código.
8. Con Carla en Servicios Pacífico (rol Consulta), comprobar lectura sin acciones de escritura.

## Coordinación de la siguiente entrega

ACUERDO-02 (#12) es compartido con Compras y Ventas. La propuesta para revisar está en
`docs/modulos/acuerdo02-propuesta.md`; todavía no constituye un acuerdo aprobado.
ACC-03 (#17) ya está implementado: servicio de contabilización con cuentas validadas del plan, partida doble,
trazabilidad, idempotencia (409 ante misma clave con otro contenido), asientos inmutables en la base y libro
diario por empresa con filtro de fechas (API y pantalla).

Queda abierto, y no depende de #17:

- **Ventas no puede confirmar todavía**: `SaleService` envía nombres de cuenta en lugar de códigos del plan.
  El cambio que necesita Casas está en [acc03-integracion-ventas.md](acc03-integracion-ventas.md).
- **PLAT-03 (#35)**: conexión y transacción compartidas para documento, stock y asiento.
- **#36 ([ACC-04])**: cuentas por empresa, períodos contables y asientos manuales; hasta entonces
  `period_closed` está declarado pero no se evalúa, y Ventas no tiene de dónde resolver sus códigos.
- **Reverso**: `ReverseAsync` responde `journal_reverse_pending` hasta acordar la corrección del documento.
