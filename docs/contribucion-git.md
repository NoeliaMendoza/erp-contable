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
