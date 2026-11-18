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
- Clases disponibles: `h1`, `h2`, `muted`, `error`, `small`, `kpi`, `sidebar*` (TextBlock); `card`, `kpi-lavender`, `kpi-sky`, `kpi-mint` (Border); `primary` (Button); `nav` (ListBox del menú).
- Logo: control `ui:OrvixLogo` (`ERP.Frontend.Shared.Controls`), escalado con `Height`; `ShowWordmark="False"` muestra solo el monograma. Archivos para documentos: `docs/assets/orvix-logo.svg` y `docs/assets/orvix-icon.svg`.

### Paleta de Orvix

| Uso | Recurso | Color |
|---|---|---|
| Primario (botones, acciones, logo) | `ErpPrimaryBrush` / hover `ErpPrimaryDarkBrush` | `#1F7A78` / `#17605E` |
| Acento lima (ítem activo, etiquetas) | `ErpAccentBrush` + texto `ErpOnAccentBrush` | `#D4F36B` + `#2F3A0A` |
| Fondo de la app | `ErpSurfaceBrush` | `#EEF1F1` |
| Tarjetas y menú lateral | `ErpCardBrush`, `ErpSidebarBrush` | `#FFFFFF` |
| Bordes | `ErpBorderBrush` | `#E3E7E9` |
| Texto / texto secundario | `ErpTextBrush` / `ErpMutedTextBrush` | `#1B1F23` / `#6B7280` |
| Indicadores (pasteles) | `ErpKpiLavenderBrush`, `ErpKpiSkyBrush`, `ErpKpiMintBrush` | `#C9B8F5`, `#A9C8FA`, `#9EE3B8` |
| Éxito / advertencia / error | `ErpSuccessBrush`, `ErpWarningBrush`, `ErpErrorBrush` | `#2E9E6B`, `#E0A12B`, `#D64545` |
| Superficies de feedback | `ErpSuccessSurfaceBrush`, `ErpSuccessBorderBrush`, `ErpErrorSurfaceBrush`, `ErpErrorBorderBrush` | `#EEF9F3`, `#B8E0C8`, `#FDECEC`, `#F3B4B4` |

- El lima **nunca** se usa como color de texto: sobre blanco no se lee. Solo como fondo con `ErpOnAccentBrush` encima.
- Sobre un pastel, el texto usa su tono oscuro (`ErpOnKpi*Brush`), no negro.
- Un solo botón `primary` por pantalla; el resto, botones normales.
- Espaciado en escala de 4: 4, 8, 12, 16, 24 (`ErpSpacing*`, `ErpPagePadding`).
- Vistas: `<Nombre>View.axaml` + `<Nombre>ViewModel.cs`, con `x:DataType` (bindings compilados).
- ViewModels heredan de `ViewModelBase` y usan `RunAsync(...)` para carga y errores.
- Textos en español, con tildes; botones con verbo: "Crear", "Guardar", "Cancelar".
- Toda acción debe poder hacerse con teclado; `AutomationProperties.Name` en campos sin etiqueta.
- Un componente que se repite en dos módulos se mueve a `ERP.Frontend.Shared` (lo coordina el Integrante 6).

### Componentes visuales compartidos

Los controles de `ERP.Frontend.Shared/Controls` concentran patrones repetidos para que cada módulo conserve la misma experiencia:

- `FormField` para etiqueta, ayuda, contenido y error de campo.
- `FeedbackBar` para carga, error recuperable y confirmación.
- `SearchBox` para búsquedas y limpieza del texto.
- `PaginationBar` para paginación controlada por el ViewModel.
- `EmptyState` para listas sin registros.
- `ConfirmationDialog` para acciones que requieren confirmación.

La galería identificada está en `src/Frontend/ERP.Frontend/Modules/Ui/Views/ComponentGalleryView.axaml`. Es una muestra local de componentes; no sustituye pruebas de persistencia ni autorización. Las pantallas `UnitsView`, `CategoriesView` y `ProductsView` demuestran la conexión de los controles a un CRUD real de Inventario.

#### Uso en un CRUD

Todos los controles se importan desde `ERP.Frontend.Shared.Controls` y reciben sus datos mediante bindings al ViewModel. El ViewModel debe exponer `IsBusy`, `ErrorMessage`, `SuccessMessage`, `IsEmpty`, las propiedades de paginación y los comandos correspondientes.

- `FormField`: coloca el control dentro de `FieldContent`; `Label`, `Hint` y `ErrorMessage` son comunes para todos los campos.

  ```xml
  <controls:FormField Label="Nombre" ErrorMessage="{Binding NameError}">
    <controls:FormField.FieldContent>
      <TextBox Text="{Binding Name}" AutomationProperties.Name="Nombre" />
    </controls:FormField.FieldContent>
  </controls:FormField>
  ```

- `FeedbackBar`: enlaza `IsBusy`, `ErrorMessage` y `SuccessMessage`; `RetryCommand` debe volver a cargar los datos.
- `SearchBox`: enlaza `SearchText` en dos vías y proporciona un `ClearSearchCommand` para limpiar el filtro.
- `PaginationBar`: enlaza `CurrentPage`, `TotalPages`, `CanGoPrevious`, `CanGoNext`, `PreviousCommand` y `NextCommand`.
- `EmptyState`: muestra el estado vacío con `IsVisible`, `Title` y `Message`; solo agrega `ActionCommand` si la pantalla permite crear registros.
- `ConfirmationDialog`: muestra la confirmación con `IsVisible`, `Message`, `ConfirmCommand`, `CancelCommand`, `IsBusy` y `ErrorMessage`. El comando de confirmación debe protegerse contra doble envío.

#### Registro de una pantalla

Para que una pantalla de módulo sea navegable, se registra en cuatro puntos:

1. Crear `<Nombre>View.axaml` y `<Nombre>ViewModel.cs` con `x:DataType`.
2. Agregar el factory del ViewModel en `src/Frontend/ERP.Frontend/Shell/PageFactory.cs`.
3. Mapear el ViewModel a su vista en `src/Frontend/ERP.Frontend/ViewLocator.cs`.
4. Agregar la entrada de menú condicionada por permiso en `src/Frontend/ERP.Frontend/Shell/ShellViewModel.cs`.

La pantalla se documenta también en `docs/modulos/<modulo>.md`, indicando su permiso, endpoint, estados de carga/vacío/error y la prueba que la cubre. Inventario sirve como referencia completa.

## 8. Git

- Ramas: `feature/<modulo>-<tarea>`, `fix/...`, `docs/...`, `test/...`, `chore/...` en minúsculas con guiones.
- Commits: `tipo(modulo): descripción en minúscula` → `feat(sales): registrar factura`.
- PR pequeño, una tarea, con la plantilla completa. Detalle en [contribucion-git.md](contribucion-git.md).

## 9. Pruebas

- Nombre: frase en español con guiones bajos que describe el comportamiento → `Asiento_descuadrado_no_se_contabiliza`.
- Estructura Arrange / Act / Assert separada por líneas en blanco.
- Datos siempre ficticios; RUC y cédulas sintéticos.
