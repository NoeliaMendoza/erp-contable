# ADR-0001: Monolito modular MVC con aislamiento por tenant en tres capas

- Estado: Aceptado (base inicial) · Fecha: 2026-11-13

## Contexto
Seis personas desarrollan módulos en paralelo; los datos de varias empresas comparten una base PostgreSQL.

## Decisión
- Un proceso ASP.NET Core y una base; un esquema + DbContext + migraciones por módulo; contratos en proyectos `*.Contracts`.
- Controladores MVC (`[ApiController]`) por módulo bajo `/api/v1`, con carpetas `Models/ Controllers/ Services/ Dtos/ Validators/ Data/` (cambiado desde Minimal APIs a pedido del equipo para seguir el patrón MVC).
- Aislamiento: autorización en API + filtros EF + RLS forzado con contexto `set_config(..., true)` por transacción; roles `erp_app` (runtime) y `erp_migrator` sin BYPASSRLS.
- Lecturas tenant-scoped siempre en transacción explícita (`InTenantTransactionAsync`); sin transacción, RLS devuelve cero filas.

## Consecuencias
+ Fallos de un filtro no exponen datos; pruebas con el rol real.
− Toda lectura abre transacción (costo menor); transacciones entre DbContext de módulos requerirán compartir conexión (documentar al implementar ACC-05).
