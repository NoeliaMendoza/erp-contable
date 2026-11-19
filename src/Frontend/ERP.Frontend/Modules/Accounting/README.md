# Módulo Accounting (escritorio)

ACC-01 (#6) implementa el plan de cuentas en `feature/accounting-plan-cuentas`.

- `AccountingApi`: consume la API real mediante `ErpApiClient` y la sesión existente.
- `AccountsViewModel`: listado paginado, búsqueda, alta/edición, jerarquía, permisos y confirmación de estado.
- `AccountsView`: reutiliza los controles compartidos de Andy y presenta errores por campo, estados y paginación.
- El selector de padre carga todas las páginas y excluye la propia cuenta y sus descendientes.
- Mientras hay una petición se impiden operaciones simultáneas y cambios del formulario.
- Si falla un cambio de estado se conservan la confirmación y el error.

La navegación está conectada en `PageFactory`, `ViewLocator` y `ShellViewModel`.
El menú requiere el módulo `accounting` y `accounting.accounts.read`; escribir requiere `.write`.
La API vuelve a validar identidad, empresa y permisos en cada operación.
`InitialAccounting` crea la tabla y RLS; el migrador ya incluye el contexto de Contabilidad.
No hay un parche adicional que aplicar.

Ver `docs/modulos/contabilidad.md` para contratos, reglas y demostración,
y `docs/modulos/acc01-validacion.md` para los resultados comprobados.
Asientos y libro diario pertenecen a #17 y todavía no se implementan en esta entrega.
