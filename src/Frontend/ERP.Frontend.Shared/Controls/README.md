# Componentes visuales compartidos

Estos controles forman la pequeña biblioteca de interfaz del escritorio. Se usan desde Avalonia y no contienen reglas de negocio, permisos ni llamadas HTTP.

## Componentes

- `FormField`: etiqueta, ayuda, contenido y error de un campo.
- `FeedbackBar`: carga, error recuperable y confirmación de una operación.
- `SearchBox`: búsqueda con acción para limpiar.
- `PaginationBar`: navegación entre páginas controlada por el ViewModel.
- `EmptyState`: estado vacío con una explicación y una acción opcional.
- `ConfirmationDialog`: contenido reutilizable para confirmar una acción.

## Ejemplo

```xml
<controls:FormField Label="Nombre" ErrorMessage="{Binding NameError}">
  <controls:FormField.FieldContent>
    <TextBox Text="{Binding Name}" AutomationProperties.Name="Nombre" />
  </controls:FormField.FieldContent>
</controls:FormField>

<controls:FeedbackBar IsBusy="{Binding IsBusy}"
                      ErrorMessage="{Binding ErrorMessage}"
                      RetryCommand="{Binding LoadCommand}" />
```

Los ViewModels siguen siendo responsables de validar datos, ejecutar comandos y llamar a la API. El cliente debe consumir `ERP.Frontend.Shared.Api.ErpApiClient`; estos controles no deben acceder a PostgreSQL ni contener credenciales.

La vista `Modules/Ui/Views/ComponentGalleryView.axaml` muestra todos los componentes con datos locales de ejemplo. La vista `Modules/Platform/Views/BranchesView.axaml` demuestra el uso de los controles con un CRUD real.
