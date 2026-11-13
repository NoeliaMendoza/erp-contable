# Base de datos compartida en Neon

Proyecto Neon del equipo: **`erp-contable-multitenant`** (rama por defecto: `production`).
Este documento no contiene credenciales: nunca se suben al repositorio.

## Por qué ramas y no una sola base

Si seis personas aplican migraciones y datos de prueba sobre la misma base, se rompen el trabajo entre sí.
Neon permite crear **ramas** (copias instantáneas e independientes de la base). Cada integrante trabaja en la suya.

```text
production            Solo demo/entrega final. Nadie desarrolla ni migra aquí.
└─ development        Integración: refleja el esquema de `main`. Solo la persona responsable migra.
   ├─ dev-noelia      Rama personal de cada integrante (copia de development).
   ├─ dev-<nombre2>   Cada uno migra, siembra y borra datos en la SUYA sin afectar a nadie.
   └─ ...
```

> Una rama de Neon **no** es una rama de GitHub: no se sincronizan solas.

| Rama | Quién migra | Datos | Conexión de la API |
|---|---|---|---|
| `production` | Responsable, solo para la entrega | Ficticios preparados | Solo la demo |
| `development` | Responsable de integración, tras cada merge a `main` con migraciones | Ficticios (`seed-dev`) | Pruebas de integración manuales |
| `dev-<nombre>` | Su dueño, cuando quiera | Los que quiera (ficticios) | Su máquina |

Para el día a día también se puede usar **PostgreSQL local en Docker** (`scripts/setup-local.ps1`): no consume horas de Neon y funciona sin internet.

---

## A. Configuración inicial (la hace UNA persona, una vez)

### A1. Invitar al equipo al proyecto
Neon Console → proyecto `erp-contable-multitenant` → **Settings → Project permissions** (`#sharing`) → *Grant access*
→ correo de cada integrante. Cada persona recibe acceso al proyecto con su propia cuenta de Neon.

> Con acceso al proyecto se pueden ver todas las ramas, incluida `production`. Acuerden en el equipo que
> **nadie toca `production` ni `development`** salvo el responsable, y protejan `production` (A2).

### A2. Proteger `production`
Settings → *Branch protection* → **Protect this branch**. Si su plan no lo permite, al menos no compartan sus credenciales.

### A3. Crear la rama `development`
Branches → **New branch** → nombre `development`, padre `production`.

### A4. Crear los roles de la aplicación (en `development`)
SQL Editor → seleccionar rama **`development`**, base `neondb`, rol dueño (p. ej. `neondb_owner`) →
pegar [`infrastructure/neon/01-roles.sql`](../infrastructure/neon/01-roles.sql) reemplazando las dos contraseñas por valores
aleatorios largos → **Run**. Las dos columnas del resultado deben ser `f` (sin superusuario, sin BYPASSRLS).

Compartan las contraseñas de `erp_migrator` y `erp_app` **por un canal privado** (nunca en GitHub, Issues ni capturas).
Se probó este script simulando a un dueño sin privilegios de superusuario, como en Neon.

### A5. Aplicar el esquema en `development`
Desde la máquina del responsable, con la conexión directa de `development`:

```powershell
$env:ConnectionStrings__ErpMigrator = "Host=<endpoint-directo-de-development>;Database=neondb;Username=erp_migrator;Password=<...>;SSL Mode=VerifyFull;Channel Binding=Require"
dotnet run --project src/Backend/ERP.DbMigrator -- migrate seed-dev
Remove-Item Env:ConnectionStrings__ErpMigrator
```

Antes de cada migración en `development`, crear una rama de respaldo (`backup-AAAAMMDD`) desde `development`.

---

## B. Cada integrante (una vez)

1. Aceptar la invitación de Neon (correo).
2. Branches → **New branch** → nombre **`dev-<su-nombre>`** (minúsculas, sin tildes), padre **`development`**.
   Hereda los roles y el esquema.
3. En la rama nueva: **Connect** → copiar solo el **host** (algo como `ep-xxxx-xxxx-123456.us-east-2.aws.neon.tech`, sin `-pooler`).
4. Ejecutar en la carpeta del repositorio:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\configurar-neon.ps1 -Branch dev-<su-nombre> -Endpoint <host-copiado> -Migrate
```

El script pide las contraseñas de forma oculta, guarda las cadenas en **User Secrets** (API por el *pooler*, migraciones por la
conexión directa) y aplica migraciones y datos ficticios en **su** rama. Se niega a configurar `production` o `development`.

## C. Día a día

- ¿Su rama quedó desordenada o desactualizada? Neon → su rama → **Reset from parent** (vuelve al estado de `development`).
- ¿Una compañera agregó una migración a `main`? `git pull` y luego
  `dotnet run --project src/Backend/ERP.DbMigrator -- migrate` (aplica solo lo nuevo en su rama).
- ¿Volver a la base local? `powershell -ExecutionPolicy Bypass -File .\scripts\setup-local.ps1`.
- CI (GitHub Actions) **no** usa Neon: crea su propio PostgreSQL temporal.

## D. Reglas

- Nunca poner credenciales en `appsettings*.json`, código, Issues ni capturas. Solo User Secrets o secretos de CI.
- Nunca ejecutar `seed-dev` en `production`.
- Nunca usar el rol dueño (`neondb_owner`) en la API ni en el migrador.
- Las ramas consumen horas de cómputo y almacenamiento del plan: borrar ramas que ya no se usen y revisar los límites del plan en Neon.
- Las contraseñas de los roles son las mismas en las ramas hijas (se copian al crearlas); si se filtra una, cambiarla en todas.

## Detalles técnicos

- API → endpoint con **pooler** (`ep-...-pooler...`): el contexto de empresa usa `set_config(..., true)` dentro de cada
  transacción, compatible con el pooling por transacción de Neon.
- Migraciones → endpoint **directo** (sin pooler).
- TLS obligatorio: `SSL Mode=VerifyFull;Channel Binding=Require`.
- Base: `neondb` (la predeterminada). Si deciden usar otra (`erp_db`), cambien el nombre en `01-roles.sql` y en `-Database`.
