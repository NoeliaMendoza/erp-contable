# Guía Git y GitHub

**GitHub Flow**: `main` protegida y siempre verde; una rama corta por Issue; PR pequeño; *Squash and merge*.

## Flujo por tarea

```bash
git switch main
git pull --ff-only origin main
git switch -c feature/accounting-journal-entries
# programar, compilar y probar
dotnet build ERP.slnx
dotnet test --solution ERP.slnx
git status
git add <archivos-de-la-tarea>
git commit -m "feat(accounting): registrar asientos en borrador"
git push -u origin feature/accounting-journal-entries
# abrir Pull Request hacia main
```

Antes de pedir revisión: `git fetch origin` y `git merge origin/main` en su rama, resolver conflictos, compilar y probar. **No** usar `push --force` sobre ramas compartidas ni sobre `main`.
Tras el merge: `git switch main && git pull --ff-only && git branch -d <rama>`.

Prefijos de rama: `feature/`, `fix/`, `test/`, `docs/`, `refactor/`, `chore/`, `ci/` + `modulo-tarea`.
Commits: `tipo(modulo): descripción` — `feat`, `fix`, `test`, `docs`, `refactor`, `chore`, `ci`.

## Protección de `main` (configurar en GitHub → Settings → Branches / Rulesets)

- Requerir PR con **1 aprobación** de otra persona y conversaciones resueltas.
- Requerir check **CI / Compilar, verificar y probar**.
- Cuando GitHub registre la primera ejecución del bot, agregar **Tarea vinculada** como check requerido.
- Bloquear force push y eliminación.
- Activar CODEOWNERS cuando se completen los usuarios reales en `.github/CODEOWNERS`.
- Activar *Secret scanning* y *Dependabot alerts* si están disponibles.

## Archivos compartidos (coordinar antes de editar)

`Directory.*.props`, `global.json`, `ERP.slnx`, `src/Backend/ERP.Api/Program.cs`, `ERP.BuildingBlocks`, `ERP.Infrastructure`,
`ERP.DbMigrator`, `ViewLocator.cs`, `PageFactory.cs`, `ErpTheme.axaml`, `.github/`. Cambios pequeños y avisados en el canal del equipo.

## Migraciones

1. Una migración por cambio coherente de esquema, generada sobre `main` actualizado (comando en el README).
2. Agregar en la misma migración las políticas RLS (`RlsSql.EnableTenantIsolation`) y grants.
3. Probar localmente `migrate` sobre base limpia + pruebas de aislamiento.
4. Si dos PRs tocan el mismo DbContext: el segundo regenera su migración tras el merge del primero (nunca editar migraciones ajenas).
5. No borrar migraciones ya aplicadas en entornos compartidos.
6. Neon: solo el responsable de integración aplica, según [neon.md](neon.md). Nunca migraciones destructivas automáticas.

## Definición de terminado

Compila sin advertencias · pruebas pertinentes (dominio/integración/aislamiento) · permisos y tenant probados ·
migración revisada · sin secretos · documentación actualizada · PR aprobado y CI verde.

## Formato y pruebas antes de cada push

El formato es automático: no hace falta ejecutar `dotnet format` a mano.

- VS Code formatea y ordena los `using` al guardar (`.vscode/settings.json`).
- Al compilar por primera vez (`dotnet build`), Git activa el hook `.githooks/pre-commit`. Desde
  entonces, cada `git commit` formatea los `.cs` agregados con las mismas reglas que revisa el CI
  y los vuelve a agregar al commit. Tarda unos segundos. Si un archivo tiene cambios sin agregar,
  el hook no lo toca y lo avisa. Para saltarlo una vez: `git commit --no-verify`.

Antes de cada push, abra Docker Desktop y ejecute desde la raíz del repositorio:

```powershell
docker info
dotnet test --solution ERP.slnx
```

Testcontainers crea y elimina un PostgreSQL temporal. Para correr solo integración, use
`dotnet test --project tests/ERP.IntegrationTests`. Estas pruebas no necesitan iniciar
Docker Compose ni conectarse a Neon. Cree los datos que necesita cada prueba dentro de
ella para que pueda ejecutarse de forma individual.

## SonarQube Cloud

En el repositorio de GitHub, abra **Settings → Secrets and variables → Actions**.
Use el secreto `SONAR_TOKEN` con permiso de análisis y las variables
`SONAR_PROJECT_KEY` y `SONAR_ORGANIZATION` con las claves del proyecto y la
organización en SonarQube Cloud. En el primer análisis, verifique en el log
que el escáner importe `coverage.xml` y confirme la cobertura en el proyecto.

Para revisar las alertas del issue #41, abra cada hallazgo y confirme su ubicación
antes de cambiarlo a **False positive**. En S2068, documente que
`infrastructure/postgres/init/01-roles.sh` lee los valores desde
`ERP_MIGRATOR_PASSWORD` y `ERP_APP_PASSWORD`, y que los nombres de parámetros de
`psql` no contienen credenciales. Para S1313, confirme que la dirección IP
pertenece a datos de prueba. Para S5332, confirme que el protocolo sin cifrar
aparece en una URL de prueba y que esa URL no se usa para transmitir datos reales.
