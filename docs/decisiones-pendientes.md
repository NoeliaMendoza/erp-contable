# Decisiones pendientes

Supuestos actuales son **explícitos y reversibles**. Cerrar cada decisión con un ADR en `docs/adr/`.

| Id | Decisión | Supuesto actual (reversible) | Impacto | Responsable |
|---|---|---|---|---|
| D-01 | ¿Un tenant = un RUC, o un tenant administra varios RUC? | El esquema permite N contribuyentes por tenant; la UI y semillas usan 1 | Claves, RLS por contribuyente, reportes | Producto + Int. 1 |
| D-02 | Modelo sucursal ↔ establecimiento ↔ punto de emisión | Separados; establecimiento puede ligarse a una sucursal | Numeración, permisos por sucursal | Int. 1 + Int. 3 |
| D-03 | Autenticación definitiva | Esquema de desarrollo `X-Dev-User` solo en Development/Testing | Bloquea despliegue real | Int. 1 (PLAT-01) |
| D-04 | Integración de ASP.NET Core Identity con `platform.users` | Tabla propia mínima | Migración de identidad | Int. 1 |
| D-05 | Precisión, escala y redondeo monetario; moneda extranjera | USD, 2 decimales en asientos; sin moneda extranjera | Esquema contable | Int. 2 + contador |
| D-06 | Política de inventario (FIFO/promedio), stock negativo, reservas | No definida | Contratos de Inventory | Int. 5 |
| D-07 | Primeras dos plantillas sectoriales | Comercio y servicios (no implementadas) | Semillas, features | Producto |
| D-08 | Criterios de aprobación/reversión de operaciones | Permisos `*.reverse`, sin flujo de aprobación | Contabilidad, ventas | Producto + Int. 2 |
| D-09 | Políticas de respaldo y retención | Ninguna automatizada | Operación | Líder |
| D-10 | Usuarios reales de GitHub y asignación definitiva | CODEOWNERS comentado | Revisiones | Equipo |
| D-11 | Rama y base de Neon (`neondb`/`erp_db`, `producción`/`prueba`) | No se conectó a Neon | Integración | Líder |
| D-12 | Nombre comercial | **Decidido:** "ERP Contable", repositorio `erp-contable`, equipo LCS | Marca | Equipo |
| D-13 | Licencias QuestPDF/ClosedXML para uso comercial | No incluidos aún | Reportes | Int. 6 |
| D-14 | Hosting de la API (no es Neon) | Local | Despliegue | Líder |
| D-15 | Rechazar RUC de sociedades si falla módulo 11 | Solo advertencia | Validación de clientes/proveedores | Int. 3 + Int. 4 |
| D-16 | Instalador/firma del cliente Avalonia y compatibilidad cliente/API | Sin instalador ni auto-actualización | Distribución (Smart App Control) | Int. 6 |

Decisiones tributarias: [ecuador/decisiones-tributarias-pendientes.md](ecuador/decisiones-tributarias-pendientes.md).
