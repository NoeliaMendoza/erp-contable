# Guía de estilo del equipo LCS

Objetivo: que el código parezca escrito por una sola persona. Lo marcado con 🔒 lo **impone la herramienta**
(el build o el CI fallan); lo demás se revisa en el Pull Request.

## 1. Antes de cada commit (30 segundos)

```bash
dotnet format ERP.slnx
dotnet build ERP.slnx
dotnet test --solution ERP.slnx
```

`dotnet format` corrige solo el formato y el orden de `using`. 🔒 El CI rechaza el PR si queda algo sin formatear.

## 2. Editor

- Visual Studio 2026, Rider o VS Code con las extensiones de `.vscode/extensions.json` (C# Dev Kit, Avalonia, EditorConfig).
- El editor lee `.editorconfig` automáticamente: **no** cambien sangrías ni reglas en su configuración personal.
- Finales de línea: los gestiona `.gitattributes`; no los conviertan a mano.

## 3. Código C#

| Elemento | Convención | Ejemplo |
|---|---|---|
| Proyecto, carpeta y namespace | Iguales; `ERP.<Capa>` / `ERP.Modules.<Modulo>` | `ERP.Modules.Sales.Services` |
| Clases, records, enums, métodos, propiedades | PascalCase 🔒 | `JournalEntry`, `CreateBranchAsync` |
| Interfaces | Prefijo `I` 🔒 | `ITenantContext` |
| Campos privados | `_camelCase` 🔒 | `_tenantContext` |
| Constantes y `static readonly` | PascalCase 🔒 | `RuntimeRole` |
| Parámetros y variables | camelCase 🔒 | `tenantId` |
| Métodos asíncronos en servicios | Sufijo `Async` + `CancellationToken` | `ListBranchesAsync(ct)` |
| Un tipo principal por archivo | Archivo = nombre del tipo | `BranchesController.cs` |
| Namespaces | `file-scoped` 🔒 | `namespace ERP.Modules.Sales.Models;` |
| `using` | Fuera del namespace, `System` primero 🔒 | — |
| Nulos | `Nullable` activado; sin `!` salvo justificación | — |
| Advertencias | Son errores 🔒 (`TreatWarningsAsErrors`) | — |

**Idioma:** identificadores en **inglés** (`Branch`, `Taxpayer`); textos visibles, mensajes de error, comentarios y documentación en **español (Ecuador)**.

## 4. Estructura MVC de cada módulo

```text
ERP.Modules.<Modulo>/
  Models/       Entidades y reglas de negocio (sin EF ni HTTP)
  Controllers/  <Recurso>Controller : ApiControllerBase   🔒 (prueba ControllerConventionsTests)
  Services/     Casos de uso; lecturas en db.InTenantTransactionAsync(...)
  Dtos/         <Recurso>Response, Create<Recurso>Request
  Validators/   <Request>Validator (FluentValidation)
  Data/         <Modulo>DbContext + Migrations/
  <Modulo>Module.cs       Registro en DI
  <Modulo>Permissions.cs  Códigos de permiso
```

Dependencias entre módulos solo vía `*.Contracts` 🔒 (`DependencyRulesTests`).

## 5. API REST

- Rutas en minúsculas, plural, con versión: `api/v1/tenant/branches`, `api/v1/sales/invoices/{id}`.
- Toda acción lleva `[RequirePermission(XPermissions.Y)]` 🔒 (salvo la lista explícita de excepciones de la prueba).
- Permisos: `modulo.recurso.accion` en minúsculas → `sales.invoices.issue`.
- Respuestas: DTO, nunca entidades. Errores: `ProblemDetails` con `code` en snake_case (`branch_code_duplicated`).
- Códigos HTTP: 200 lectura · 201 creación (`CreatedAtAction`) · 400 validación · 403 permiso · 404 inexistente o de otra empresa · 422 regla de negocio.
- JSON en camelCase (por defecto de ASP.NET Core).

## 6. Base de datos

- Esquema por módulo en minúsculas (`sales`); tablas y columnas en `snake_case` plural (`invoice_lines`), generado por la convención de EF.
- Toda tabla privada: `tenant_id` + RLS con `RlsSql.EnableTenantIsolation` + prueba negativa.
- Montos: `decimal` / `numeric`; instantes `DateTimeOffset` UTC; fechas fiscales `DateOnly`.
- Migraciones: `Fecha_NombreEnPascalCase` (lo genera EF), una por cambio coherente.

## 7. Interfaz (Avalonia)

- Colores, tamaños y espaciados **solo** desde `ERP.Frontend.Shared/Styles/ErpTheme.axaml` 🔒 (prueba `Las_vistas_usan_el_tema_comun...`).
- Clases disponibles: `h1`, `h2`, `muted`, `error`, `small`, `kpi`, `sidebar*` (TextBlock); `card` (Border); `primary` (Button).
- Espaciado en escala de 4: 4, 8, 12, 16, 24 (`ErpSpacing*`, `ErpPagePadding`).
- Vistas: `<Nombre>View.axaml` + `<Nombre>ViewModel.cs`, con `x:DataType` (bindings compilados).
- ViewModels heredan de `ViewModelBase` y usan `RunAsync(...)` para carga y errores.
- Textos en español, con tildes; botones con verbo: "Crear", "Guardar", "Cancelar".
- Toda acción debe poder hacerse con teclado; `AutomationProperties.Name` en campos sin etiqueta.
- Un componente que se repite en dos módulos se mueve a `ERP.Frontend.Shared` (lo coordina el Integrante 6).

## 8. Git

- Ramas: `feature/<modulo>-<tarea>`, `fix/...`, `docs/...`, `test/...`, `chore/...` en minúsculas con guiones.
- Commits: `tipo(modulo): descripción en minúscula` → `feat(sales): registrar factura`.
- PR pequeño, una tarea, con la plantilla completa. Detalle en [contribucion-git.md](contribucion-git.md).

## 9. Pruebas

- Nombre: frase en español con guiones bajos que describe el comportamiento → `Asiento_descuadrado_no_se_contabiliza`.
- Estructura Arrange / Act / Assert separada por líneas en blanco.
- Datos siempre ficticios; RUC y cédulas sintéticos.
