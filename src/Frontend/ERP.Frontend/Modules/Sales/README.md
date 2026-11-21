# Módulo Sales (escritorio)

# Módulo Ventas

La pantalla confirma ventas al contado desde `/api/v1/sales/confirm`.

- Valida sesión y permiso `sales.invoices.issue` en el servidor.
- Toma el precio vigente del catálogo; no permite precios enviados desde el escritorio.
- Previene doble envío por estado local e `IdempotencyKey`.
- Muestra borrador/confirmada y referencia técnica en los errores de API.
- El estado electrónico empieza en `NotRequested`. ACUERDO-03 limita esta entrega a simulación;
  no debe mostrarse como autorización del SRI hasta tener una respuesta verificable real.

## Clientes (CRUD)

- Pantalla conectada a `/api/v1/customers`.
- Permisos `sales.customers.read` y `sales.customers.write`.
- Tipos: RUC, cédula, pasaporte y consumidor final.
- Validación ecuatoriana mostrada como advertencia; no bloquea el registro.
