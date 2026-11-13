# Módulo Sales (escritorio)

Carpeta reservada para las vistas (`Views/`) y ViewModels (`ViewModels/`) del módulo Sales.

1. Crear ViewModels heredando de `ViewModelBase` (ERP.Frontend.Shared.Mvvm).
2. Registrar el par VM → Vista en `ViewLocator.cs` y la página en `Shell/PageFactory.cs`.
3. Consumir solo la API REST mediante métodos nuevos en `ErpApiClient` (ERP.Frontend.Shared).
4. Ocultar acciones según permisos es solo UX: la API valida todo.

Ver `docs/contratos-modulos.md`.
