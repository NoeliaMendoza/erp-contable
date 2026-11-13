# Modelo de datos (esquema `platform`, migración `InitialPlatform`)

Conceptos distintos — **no** equivalentes:

| Concepto | Tabla | Notas |
|---|---|---|
| Organización SaaS (tenant) | `tenants` | Cliente del SaaS. |
| Persona usuaria (global) | `users` | Sin `tenant_id`. Clasificada **global**. |
| Pertenencia | `tenant_memberships` | Un usuario puede pertenecer a varios tenants. |
| Rol / permisos por tenant | `roles`, `role_permissions`, `membership_roles` | Permisos = códigos del catálogo de cada módulo. |
| Módulos habilitados | `tenant_features` | Feature flags por tenant. |
| Sucursal física | `branches` | Operativa; **no** es establecimiento SRI. |
| Contribuyente (RUC) | `taxpayers` | `(tenant_id, ruc)` único. ¿Más de un RUC por tenant? → D-01. |
| Establecimiento SRI | `establishments` | Código 3 dígitos por contribuyente; opcionalmente ligado a una sucursal. |
| Punto de emisión SRI | `emission_points` | Código 3 dígitos por establecimiento. Secuenciales por tipo de comprobante: Ventas/SRI (pendiente). |

## Reglas de integridad

- Toda tabla privada tiene `tenant_id NOT NULL` + RLS.
- **Claves foráneas compuestas** `(tenant_id, x_id) → (tenant_id, id)` impiden relaciones cruzadas entre tenants
  (role_permissions, membership_roles, establishments, emission_points).
- Únicos compuestos por tenant: `(tenant_id, code)` en sucursales, `(tenant_id, ruc)`, `(tenant_id, taxpayer_id, code)`, etc.
- Identificadores `uuid` v7 generados en la aplicación (`Guid.CreateVersion7()`).
- Instantes `timestamptz` (UTC); fechas fiscales `date`.
- Importes: `numeric` con escala definida por Contabilidad (provisional: 2 decimales USD, D-05).

## Clasificación de tablas

| Tabla | Clasificación | RLS |
|---|---|---|
| users | Global (identidad) | No (runtime: SELECT/INSERT/UPDATE, sin DELETE) |
| tenants | Raíz de tenant | Sí: tenant activo **o** miembro autenticado |
| tenant_memberships | Puente | Sí: tenant activo **o** propias del usuario; escritura solo tenant activo |
| resto | Tenant-scoped | Sí: `tenant_id = current_tenant_id()` (USING + WITH CHECK) |
