# Módulo Inventario — catálogo (INV-01)

Responsable: **Mendoza** · Rama: `feature/inventory-productos` · Esquema PostgreSQL: `inventory`

## Alcance de esta entrega

| Incluye | NO incluye todavía |
|---|---|
| Unidades de medida (CRUD + baja lógica) | Existencias / stock |
| Productos y servicios (CRUD + baja lógica) | Kardex, entradas y salidas |
| Búsqueda, filtro por tipo, paginación | Bodegas, reservas, costeo |
| Contrato `IProductCatalog` para Ventas | Impuestos, precios históricos |

> **El catálogo no calcula existencias.** No hay un campo "stock" editable a propósito: el stock será el
> resultado de los movimientos de inventario (INV-03), nunca un número digitado.

## Reglas

| Regla | Detalle |
|---|---|
| Código de unidad | Texto de 1 a 10 caracteres (`A-Z 0-9 . _ -`). Se recortan espacios y se guarda en MAYÚSCULAS. Único por empresa |
| SKU | Texto de 1 a 40 caracteres, mismas reglas de normalización. Único por empresa. El mismo SKU puede existir en otra empresa |
| Tipo | `Product` (producto/mercadería) o `Service` (servicio) |
| Unidad | Obligatoria **también para servicios** (p. ej. `HORA`, `SERV`): política uniforme |
| Precio | Precio de venta en USD, `decimal` / `numeric(18,4)`, máximo 4 decimales, no negativo. **No se redondea en silencio**: más decimales → error 400 |
| Unidad de otra empresa | Se rechaza (422 `unit_not_available`) aunque su ID exista. Además, una FK compuesta `(tenant_id, unit_id)` lo impide en la base |
| Unidad inactiva | No se asigna a artículos nuevos (422 `unit_inactive`). Un artículo existente puede conservarla |
| Desactivar una unidad | Prohibido si la usan artículos **activos** (409 `unit_in_use`): primero se desactivan o cambian esos artículos |
| Reactivar un artículo | Exige que su unidad esté activa |
| Borrado | No existe. Solo baja lógica (`isActive`) para conservar el historial |
| Auditoría | `created_at_utc`, `created_by`, `updated_at_utc`, `updated_by` en cada fila |

## API (v1)

Encabezado `X-Tenant-Id` obligatorio. `TenantId` nunca viaja en el cuerpo: lo fija el servidor.

| Método | Ruta | Permiso |
|---|---|---|
| GET | `/api/v1/units?page&pageSize&search&isActive` | `inventory.units.read` |
| GET | `/api/v1/units/{id}` | `inventory.units.read` |
| POST | `/api/v1/units` `{ code, name }` | `inventory.units.write` |
| PUT | `/api/v1/units/{id}` `{ code, name }` | `inventory.units.write` |
| PATCH | `/api/v1/units/{id}/status` `{ isActive }` | `inventory.units.write` |
| GET | `/api/v1/products?page&pageSize&search&kind&isActive` | `inventory.products.read` |
| GET | `/api/v1/products/{id}` | `inventory.products.read` |
| POST | `/api/v1/products` `{ sku, name, kind, unitId, price }` | `inventory.products.write` |
| PUT | `/api/v1/products/{id}` `{ sku, name, kind, unitId, price }` | `inventory.products.write` |
| PATCH | `/api/v1/products/{id}/status` `{ isActive }` | `inventory.products.write` |

Ejemplo de respuesta de listado:

```json
{
  "items": [
    { "id": "…", "sku": "CUAD-001", "name": "Cuaderno universitario 100 hojas", "kind": "Product",
      "unitId": "…", "unitCode": "UND", "unitName": "Unidad", "price": 5.0000, "isActive": true }
  ],
  "totalCount": 1, "page": 1, "pageSize": 20
}
```

Errores: 400 validación (con `errors` por campo) · 401 sin sesión · 403 sin permiso o empresa ajena ·
404 no existe en la empresa activa · 409 duplicado o unidad en uso · 422 regla de negocio (unidad inactiva/ajena).

## Para Ventas (Casas): contrato `IProductCatalog`

```csharp
// ERP.Modules.Inventory.Contracts
Task<IReadOnlyList<ProductSummary>> GetByIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);
// ProductSummary(ProductId, Code = SKU, Name, IsService, UnitCode, IsActive)
```

Devuelve solo artículos de la empresa activa; IDs ajenos o inexistentes no aparecen. Ventas debe tomar
el precio de la venta desde su propio documento (el precio del catálogo es una sugerencia) y rechazar
artículos inactivos. Un servicio (`IsService = true`) no generará salida de stock.

## Pantallas (Avalonia)

- **Productos y servicios**: búsqueda, filtro por tipo, incluir inactivos, paginación, formulario con
  selector de unidades activas, precio con coma o punto, errores junto al campo y confirmación antes de
  activar/desactivar.
- **Unidades**: mismo patrón.
- Aparecen en el menú si la empresa tiene el módulo `inventory` habilitado y el usuario tiene permiso de lectura;
  los botones de escritura se ocultan sin permiso (solo UX: la API valida igual).

## Pruebas

- `tests/ERP.IntegrationTests/Api/InventoryApiTests.cs`: duplicados, mismo SKU en otra empresa, precio
  negativo o con más de 4 decimales, tipo inválido, unidad ajena o inactiva, unidad en uso, baja lógica,
  filtro por tipo, aislamiento por ID (GET/PUT/PATCH), rol de consulta, 401/400, paginación y búsqueda con comodines.
- `tests/ERP.IntegrationTests/Security/RowLevelSecurityTests.cs`: RLS con el rol real en `inventory.*` y FK cruzada.
- `tests/ERP.UnitTests/Inventory/InventoryModelTests.cs` y `tests/ERP.UnitTests/Frontend/InventoryFrontendTests.cs`.

## Datos de demostración

`seed-dev` crea en **Comercial Andina Demo**: unidades `UND` y `HORA`; artículos `CUAD-001` (producto, USD 5)
y `SRV-INST` (servicio, USD 25). En bases ya sembradas, `seed-dev` también agrega los permisos nuevos a los roles
del sistema (Administrador y Consulta).

## Siguiente entrega (propuesta)

Entradas, salidas y existencias con trazabilidad al documento (INV-02/03); valoración por promedio móvil a
acordar con Romero; coordinación con Torres (recepción) y Casas (venta). Verificación del ejemplo:
entrada 10, salida 4, saldo 6 y costo de salida 12.

## Ficha del artículo (paquete 1)

| Campo | Regla |
|---|---|
| IVA (`vatTreatment`) | **Obligatorio**: `Standard` (gravado, tarifa general), `ZeroRated` (0 %), `Exempt` (exento), `NotSubject` (no objeto). No se guarda ningún porcentaje: Ventas resuelve código SRI y porcentaje vigente a la fecha de la factura con `TaxRateCatalog` |
| Categoría (`categoryId`) | Opcional. Debe ser de la misma empresa (422 `category_not_available`) y estar activa (422 `category_inactive`). FK compuesta `(tenant_id, category_id)` |
| Código de barras (`barcode`) | Opcional, máx. 25 (igual que el código auxiliar del SRI), MAYÚSCULAS, único por empresa (409 `product_barcode_duplicated`). La búsqueda también lo usa |
| Descripción | Opcional, máx. 300 (igual que la descripción del detalle de la factura) |
| Costo (`cost`) | Opcional, USD, ≥ 0, máx. 4 decimales. Solo referencial (margen); el costo real vendrá del kardex |
| Controla inventario (`tracksInventory`) | Por defecto: producto sí, servicio no. Un servicio **nunca** controla inventario |
| Stock mínimo (`minStock`) | Opcional, ≥ 0, solo si controla inventario |

Categorías: `GET/POST/PUT/PATCH /api/v1/categories` (`inventory.categories.read` / `.write`). Nombre único por empresa sin
distinguir mayúsculas (409 `category_name_duplicated`); no se desactiva si la usan artículos activos (409 `category_in_use`).
La base repite las reglas con CHECK (`ck_products_*`), RLS en `inventory.categories` e índice único `upper(name)`.
Migración: `InventoryProductSheet` (las filas existentes quedan con IVA `Standard` solo como dato de demostración).
