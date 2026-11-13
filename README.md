# ERP Contable

Base técnica (*foundation*) de un ERP contable SaaS multi-tenant de escritorio para contribuyentes en Ecuador.
Monolito modular **.NET 10 LTS** · **ASP.NET Core** · **PostgreSQL + RLS** · **Avalonia** · **xUnit v3**.

> Proyecto del equipo **LCS**. Repositorio: `erp-contable`. Este repositorio **no** declara cumplimiento ni homologación con el SRI.

## Estado

| Área | Estado |
|---|---|
| Solución, dependencias fijadas (CPM), estilo, CI | **Implementado** |
| API: ProblemDetails, health checks, OpenAPI, Serilog, OpenTelemetry, correlation id | **Implementado** |
| Multi-tenant: resolución de tenant + filtros EF + RLS forzado + roles PostgreSQL separados | **Implementado y probado** |
| Plataforma: tenants, usuarios, membresías, roles/permisos por empresa, sucursales, contribuyentes (RUC), establecimientos, puntos de emisión | **Implementado** (lectura + alta de sucursales) |
| Flujo vertical Avalonia → API → PostgreSQL con 2 empresas ficticias | **Implementado** (API probada; escritorio compila, ver nota Windows) |
| Autenticación | **Parcial**: solo esquema de desarrollo `X-Dev-User`. OIDC + PKCE **pendiente** (PLAT-01) |
| Contabilidad | **Parcial**: dominio de asientos (partida doble, inmutabilidad, reversos) probado; persistencia/API pendientes |
| Tributario (Ecuador) | **Parcial**: validación sintáctica RUC/cédula, catálogo tributario versionado (sin valores oficiales), contratos SRI. Sin integración SRI |
| Ventas, Compras/Tesorería, Inventario, Reportes | **Pendiente** (proyectos, permisos y contratos listos) |

## Estructura

```text
ERP.slnx
src/Backend/
  ERP.Api                       Host HTTP: composición, seguridad transversal, middleware
  ERP.BuildingBlocks            Abstracciones mínimas (ITenantContext, permisos, errores de dominio)
  ERP.Infrastructure            TenantSession, interceptor RLS, DbContext base, caché segregada, SQL RLS estándar
  ERP.Tributario                Validación RUC/cédula, catálogo tributario por vigencia, contratos SRI
  ERP.Modules.Platform          Integrante 1 — Models/ Controllers/ Services/ Dtos/ Validators/ Data/
  ERP.Modules.Accounting(+.Contracts)   Integrante 2
  ERP.Modules.Sales             Integrante 3
  ERP.Modules.Purchases         Integrante 4
  ERP.Modules.Inventory(+.Contracts)    Integrante 5
  ERP.Modules.Reporting         Integrante 6
  ERP.DbMigrator                Aplica migraciones (rol migrator) y semillas ficticias
src/Frontend/
  ERP.Frontend                   Host Avalonia (Shell + Modules/<Modulo>)
  ERP.Frontend.Shared            Cliente HTTP tipado, sesión, MVVM base, tema
tests/  ERP.UnitTests · ERP.IntegrationTests (PostgreSQL real) · ERP.ArchitectureTests
infrastructure/  docker-compose.yml · postgres/init · env.example
docs/   arquitectura, seguridad, contratos, git, decisiones, backlog, ecuador/, adr/
```

## Requisitos

- .NET SDK 10.0.1xx (ver `global.json`)
- Docker Desktop (PostgreSQL/Redis locales y pruebas de integración)
- Git

## Puesta en marcha local (Windows / PowerShell)

```powershell
# 1. Servicios locales (PostgreSQL 17 + Redis). Edite las contraseñas del .env.
Copy-Item infrastructure\env.example infrastructure\.env
docker compose -f infrastructure/docker-compose.yml --env-file infrastructure/.env up -d

# 2. Secretos locales (fuera del repositorio). Use las contraseñas de infrastructure\.env
dotnet user-secrets set "ConnectionStrings:ErpMigrator" "Host=localhost;Port=5432;Database=erp;Username=erp_migrator;Password=<ERP_MIGRATOR_PASSWORD>" --project src/Backend/ERP.DbMigrator
dotnet user-secrets set "ConnectionStrings:ErpRuntime" "Host=localhost;Port=5432;Database=erp;Username=erp_app;Password=<ERP_APP_PASSWORD>" --project src/Backend/ERP.Api
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project src/Backend/ERP.Api

# 3. Migraciones + datos ficticios (dos empresas, tres usuarios)
dotnet tool restore
dotnet run --project src/Backend/ERP.DbMigrator -- migrate seed-dev

# 4. API (https://localhost:7180, OpenAPI en /openapi/v1.json)
dotnet dev-certs https --trust
dotnet run --project src/Backend/ERP.Api --launch-profile https

# 5. Escritorio (otra terminal)
dotnet run --project src/Frontend/ERP.Frontend
```

Usuarios ficticios (modo desarrollo, sin contraseña):

| Usuario | Comercial Andina Demo S.A. | Servicios Pacífico Demo |
|---|---|---|
| `ana.admin@demo.local` | Administrador | — |
| `bruno.admin@demo.local` | — | Administrador |
| `carla.contadora@demo.local` | Administrador | Consulta |

Prueba rápida con la API:

```powershell
curl.exe -H "X-Dev-User: carla.contadora@demo.local" https://localhost:7180/api/v1/me
curl.exe -H "X-Dev-User: ana.admin@demo.local" -H "X-Tenant-Id: 0195a000-0000-7000-8000-00000000a001" https://localhost:7180/api/v1/tenant/taxpayers
```

## Pruebas

```powershell
dotnet test --solution ERP.slnx          # todas (integración requiere Docker en ejecución)
dotnet test --project tests/ERP.UnitTests
```

Se usa **Microsoft Testing Platform** (configurado en `global.json`), por eso `dotnet test` lleva `--solution`/`--project`.

### Nota: Windows Smart App Control

Si Windows tiene **Smart App Control** activado, puede bloquear DLL compiladas localmente o sin firma
(error `0x800711C7 ... Una directiva de Control de aplicaciones bloqueó este archivo`). Esto ocurrió en el equipo
donde se creó la base (bloqueó `ERP.Modules.Platform.dll` y `Avalonia.Desktop.dll`). Opciones: desactivarlo en
*Seguridad de Windows → Control de aplicaciones y navegador* (decisión personal; revise las implicaciones),
o compilar/probar dentro del contenedor del SDK:

```powershell
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test --solution ERP.slnx
```

(las pruebas de integración dentro de un contenedor requieren además montar el socket de Docker).

## Migraciones (EF Core)

```powershell
dotnet ef migrations add <Nombre> --project src/Backend/ERP.Modules.<Modulo> --startup-project src/Backend/ERP.DbMigrator --context <Modulo>DbContext --output-dir Data/Migrations
```

Revise el SQL, agregue políticas RLS (`RlsSql.EnableTenantIsolation`) y siga [docs/contribucion-git.md](docs/contribucion-git.md#migraciones).
Neon: [docs/neon.md](docs/neon.md). Nunca aplicar migraciones a la rama estable de Neon desde una máquina personal.

## Documentación

- [Arquitectura y mapa de módulos](docs/arquitectura.md) · [Modelo de datos](docs/modelo-datos.md)
- [Seguridad multi-tenant](docs/seguridad-multitenant.md) · [Contratos entre módulos](docs/contratos-modulos.md)
- [Guía de estilo](docs/guia-estilo.md) · [Guía Git/GitHub](docs/contribucion-git.md) · [Backlog de Issues para 6 integrantes](docs/backlog-issues.md)
- [Decisiones pendientes](docs/decisiones-pendientes.md) · [ADR](docs/adr/)
- Ecuador: [requisitos SRI](docs/ecuador/requisitos-sri.md) · [matriz normativa](docs/ecuador/matriz-normativa.md) · [facturación electrónica](docs/ecuador/facturacion-electronica.md) · [decisiones tributarias pendientes](docs/ecuador/decisiones-tributarias-pendientes.md)
