# Backlog de Issues sugeridos (6 integrantes)

> Sugeridos, **no creados** en GitHub. Asignación provisional; balancear por estimación real.
> Formato: Id · objetivo · archivos probables · depende de · criterios de aceptación (CA) · pruebas.

## Matriz de trabajo en paralelo

| Integrante | Puede empezar ya | Bloqueado por |
|---|---|---|
| 1 Platform | PLAT-01..05 | — |
| 2 Accounting | ACC-01..04 (dominio ya existe) | — ; ACC-05 necesita ACC-02 |
| 3 Sales + SRI | SAL-01, SAL-02, SRI-01 | SAL-04 necesita ACC-05 e INV-04 |
| 4 Purchases/Treasury | PUR-01, PUR-02, TRE-01 | PUR-04 necesita ACC-05 |
| 5 Inventory | INV-01..03 | INV-05 necesita ACC-05 |
| 6 Reporting/UX | UX-01..03, REP-01 | REP-03 necesita consultas de módulos |

## Integrante 1 — Plataforma y multi-tenancy
- **PLAT-01 OIDC de escritorio.** Identity + OpenIddict, Authorization Code + PKCE con navegador del sistema, refresh/revocación, almacenamiento protegido del token en Avalonia. Archivos: `ERP.Api/Security`, `ERP.Modules.Platform`, `ERP.Frontend.Shared/Api`. CA: sin cabecera X-Dev-User fuera de Development; token expira y se renueva. Pruebas: integración de login/token, 401 con token inválido.
- **PLAT-02 Administración de usuarios y membresías** (invitar, suspender, asignar roles). CA: solo `platform.users.manage`; no se puede asignar rol de otro tenant. Pruebas: negativas entre tenants.
- **PLAT-03 Roles y permisos editables** con validación contra el catálogo `IPermissionProvider`.
- **PLAT-04 Activación de módulos por tenant y plantillas de negocio** (BusinessTemplate).
- **PLAT-05 Auditoría** (actor, tenant, acción, referencia, cambios; sin secretos).
- **PLAT-06 CRUD de contribuyentes/establecimientos/puntos de emisión** con validación RUC y unicidad.

## Integrante 2 — Contabilidad
- **ACC-01 Plan de cuentas** por tenant (jerarquía, tipos, cuentas de movimiento). Esquema `accounting`, `AccountingDbContext`, RLS.
- **ACC-02 Períodos contables**: abrir/cerrar/bloquear; consulta "¿período abierto?".
- **ACC-03 Persistencia de asientos** (borrador → contabilizado → reversado) usando el dominio existente; `numeric(18,2)`.
- **ACC-04 Libro diario y mayor** con paginación en servidor.
- **ACC-05 Implementar `IAccountingPostingService`** (idempotencia por clave única `(tenant_id, idempotency_key)`, concurrencia). Pruebas: reintento no duplica; descuadre rechazado; período cerrado rechazado.
- **ACC-06 Mapeo de cuentas por operación** (IVA ventas, CxC, etc.) acordado con Int. 3 y 4.
- **ACC-07 Balance de comprobación** y estados básicos.

## Integrante 3 — Ventas y adaptación SRI
- **SAL-01 Clientes** (identificación RUC/cédula/pasaporte/consumidor final según catálogo verificado).
- **SAL-02 Documento de venta comercial** (borrador, líneas, totales, cálculo de impuestos vía `TaxRateCatalog`).
- **SAL-03 Secuenciales por establecimiento/punto de emisión/tipo** con concurrencia (bloqueo de fila). Prueba de carrera.
- **SAL-04 Emisión**: venta + movimiento de inventario + asiento en una transacción; rollback total ante fallo.
- **SAL-05 Cuentas por cobrar** básicas.
- **SRI-01 Verificar ficha técnica vigente** y completar `docs/ecuador/matriz-normativa.md` (códigos, XSD, clave de acceso).
- **SRI-02 Generador de clave de acceso + XML factura** contra XSD, con simulador. Sin emisión real.

## Integrante 4 — Compras y tesorería
- **PUR-01 Proveedores** · **PUR-02 Registro de compras** · **PUR-03 Retenciones** (motor por reglas versionadas; no universales) ·
  **PUR-04 Integración contable de compras** · **TRE-01 Caja y bancos** · **TRE-02 Pagos y cobros** · **TRE-03 Conciliación básica**.

## Integrante 5 — Inventario
- **INV-01 Productos/servicios y unidades** · **INV-02 Bodegas** · **INV-03 Movimientos y existencias** (concurrencia) ·
  **INV-04 Implementar Inventory.Contracts** · **INV-05 Kardex y costeo** (tras D-06) · **INV-06 Devoluciones**.

## Integrante 6 — Reportes y diseño compartido
- **UX-01 Guía visual y controles reutilizables** (tabla paginada, formulario, diálogo de confirmación, mensajes).
- **UX-02 Navegación por teclado y accesibilidad** · **UX-03 Formato es-EC de USD y fechas** en controles.
- **REP-01 Dashboard** · **REP-02 Exportación PDF/Excel** (tras D-13) · **REP-03 Conciliaciones tributarias internas** (ventas, compras, IVA, retenciones; no son declaraciones oficiales).
- **QA-01 (rotativo)** pruebas de aislamiento en reportes, exportaciones y caché.
