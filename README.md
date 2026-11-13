# ERP Contable

Base técnica (*foundation*) de un ERP contable SaaS multi-tenant de escritorio para contribuyentes en Ecuador.
Monolito modular **.NET 10 LTS** · **ASP.NET Core** · **PostgreSQL + RLS** · **Avalonia** · **xUnit v3**.

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
infrastructure/  docker-compose.yml · postgres/init · neon/01-roles.sql · env.example
scripts/  setup-local.ps1 (entorno local) · configurar-neon.ps1 (rama personal Neon)
docs/   arquitectura, seguridad, contratos, git, decisiones, backlog, ecuador/, adr/
```

## Guía de configuración (clonar y ejecutar)

Pensada para Windows 10/11 con PowerShell. Tiempo estimado: 20–30 minutos la primera vez.

### Paso 1 — Instalar requisitos (una vez)

| Herramienta | Para qué | Instalación |
|---|---|---|
| Git | Clonar y versionar | `winget install --id Git.Git -e` |
| .NET SDK 10 | Compilar backend y front | `winget install --id Microsoft.DotNet.SDK.10 -e` |
| Docker Desktop | PostgreSQL/Redis locales y pruebas de integración | `winget install --id Docker.DockerDesktop -e` (luego abrirlo) |
| Editor | Programar | Visual Studio 2026, Rider o VS Code (acepte las extensiones recomendadas del repo) |

Verificar en una terminal **nueva**:

```powershell
git --version
dotnet --list-sdks     # debe aparecer una versión 10.0.x
docker info            # no debe dar error (Docker Desktop abierto)
```

Configure su identidad de Git, con el mismo correo de su cuenta de GitHub:

```powershell
git config --global user.name "Su Nombre Apellido"
git config --global user.email "su-correo@ejemplo.com"
```

### Paso 2 — Clonar el repositorio

Primero acepte la invitación al repositorio que llega a su correo de GitHub.

```powershell
git clone https://github.com/NoeliaMendoza/erp-contable.git
cd erp-contable
dotnet build ERP.slnx      # debe terminar sin errores ni advertencias
```

### Paso 3 — Base de datos (elegir una opción)

**Opción A — Local con Docker (recomendada para empezar).** Un solo comando: crea `infrastructure/.env` con contraseñas
aleatorias, levanta PostgreSQL y Redis, guarda las conexiones en User Secrets y carga datos ficticios.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-local.ps1
```

**Opción B — Su rama personal en Neon (base en la nube del equipo).**
Cada integrante trabaja en **su propia rama** (copia de `development`); nadie comparte base con otro ni toca `production`.

1. **Acepte la invitación de Neon** que llega a su correo. La envía la administradora del proyecto desde
   *Neon → Settings → Project permissions*. Si no le llegó, pídasela.
2. **Pida por mensaje privado** (nunca en GitHub ni en el grupo general) las contraseñas de los usuarios `erp_migrator` y `erp_app`.
3. En [console.neon.tech](https://console.neon.tech) → proyecto **`erp-contable-multitenant`** → **Branches** → **New branch**:
   - Nombre: `dev-<su-nombre>` en minúsculas y sin tildes (ej.: `dev-carlos`).
   - Rama padre (*parent*): **`development`** ⚠️ no `production`.
4. Entre a su rama → **Connect** → copie solo el **host**: el texto que empieza con `ep-` y termina en `.neon.tech`.
   No copie la contraseña ni la cadena completa.
5. En la carpeta del proyecto ejecute (cambie su nombre y el host):

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\configurar-neon.ps1 -Branch dev-<su-nombre> -Endpoint <host-copiado> -Migrate
   ```

   Escriba las dos contraseñas cuando las pida (no se ven al escribir). Debe terminar con
   `Migraciones aplicadas.` y `Semillas de desarrollo cargadas (datos ficticios).`

Si su rama se desordena: Neon → su rama → **Reset from parent**. Más detalle en [docs/neon.md](docs/neon.md).

Puede cambiar entre A y B cuando quiera: cada script reescribe sus User Secrets.

### Paso 4 — Ejecutar la API

```powershell
dotnet dev-certs https --trust                                   # una vez; acepte el aviso
dotnet run --project src/Backend/ERP.Api --launch-profile https  # https://localhost:7180
```

Comprobar en otra terminal:

```powershell
curl.exe https://localhost:7180/health/ready
curl.exe -H "X-Dev-User: carla.contadora@demo.local" https://localhost:7180/api/v1/me
```

La especificación OpenAPI está en `https://localhost:7180/openapi/v1.json`.

### Paso 5 — Front (opcional por ahora)

```powershell
dotnet run --project src/Frontend/ERP.Frontend
```

### Paso 6 — Pruebas

```powershell
dotnet test --solution ERP.slnx    # requiere Docker abierto (crea un PostgreSQL temporal)
```

### Paso 7 — Empezar a trabajar

Lea [la guía de estilo](docs/guia-estilo.md) y [la guía Git](docs/contribucion-git.md), tome su Issue y cree su rama:

```powershell
git switch -c feature/<modulo>-<tarea>
```

### Problemas frecuentes

| Síntoma | Solución |
|---|---|
| `la ejecución de scripts está deshabilitada` | Ejecute los scripts con `powershell -ExecutionPolicy Bypass -File ...` como se muestra arriba |
| `Docker no está en ejecución` / `docker info` falla | Abra Docker Desktop y espere a que diga *Engine running* |
| Puerto 5432 o 6379 ocupado | Cambie `POSTGRES_PORT` / `REDIS_PORT` en `infrastructure/.env` y vuelva a ejecutar `setup-local.ps1` |
| `Falta ConnectionStrings:ErpRuntime` | No se configuró la base: ejecute el Paso 3 |
| Error de certificado HTTPS | `dotnet dev-certs https --clean` y luego `dotnet dev-certs https --trust` |
| `Control de aplicaciones bloqueó este archivo` (0x800711C7) | Smart App Control de Windows; ver la nota más abajo |
| `dotnet --list-sdks` no muestra 10.0.x | Instale el SDK 10 y abra una terminal nueva |

### Configuración manual (sin scripts)

```powershell
Copy-Item infrastructure\env.example infrastructure\.env   # y cambie las contraseñas
docker compose -f infrastructure/docker-compose.yml --env-file infrastructure/.env up -d
dotnet user-secrets set "ConnectionStrings:ErpMigrator" "Host=localhost;Port=5432;Database=erp;Username=erp_migrator;Password=<ERP_MIGRATOR_PASSWORD>" --project src/Backend/ERP.DbMigrator
dotnet user-secrets set "ConnectionStrings:ErpRuntime" "Host=localhost;Port=5432;Database=erp;Username=erp_app;Password=<ERP_APP_PASSWORD>" --project src/Backend/ERP.Api
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project src/Backend/ERP.Api
dotnet tool restore
dotnet run --project src/Backend/ERP.DbMigrator -- migrate seed-dev
```

## Datos de prueba

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
Neon: [docs/neon.md](docs/neon.md). En su rama `dev-<nombre>` puede migrar libremente; `development` y `production` solo las migra el responsable.

## Documentación

- [Arquitectura y mapa de módulos](docs/arquitectura.md) · [Modelo de datos](docs/modelo-datos.md)
- [Seguridad multi-tenant](docs/seguridad-multitenant.md) · [Contratos entre módulos](docs/contratos-modulos.md)
- [Guía de estilo](docs/guia-estilo.md) · [Guía Git/GitHub](docs/contribucion-git.md) · [Backlog de Issues para 6 integrantes](docs/backlog-issues.md)
- [Decisiones pendientes](docs/decisiones-pendientes.md) · [ADR](docs/adr/)
- Ecuador: [requisitos SRI](docs/ecuador/requisitos-sri.md) · [matriz normativa](docs/ecuador/matriz-normativa.md) · [facturación electrónica](docs/ecuador/facturacion-electronica.md) · [decisiones tributarias pendientes](docs/ecuador/decisiones-tributarias-pendientes.md)
