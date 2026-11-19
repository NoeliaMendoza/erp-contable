# ACUERDO-02 (#12): propuesta para revisar con Compras y Ventas

Estado: propuesta de Romero, pendiente de validación de Noe y los responsables de Compras y Ventas.
No cambia interfaces públicas ni se considera un acuerdo aprobado.

## Contrato existente

Se conserva `ERP.Modules.Accounting.Contracts.IAccountingPostingService`:

- `PostAsync(PostingRequest, CancellationToken)` devuelve el ID del asiento y `AlreadyProcessed`.
- `ReverseAsync(...)` ya está declarado; no se habilitan correcciones sin definir su política.
- La solicitud conserva fecha contable, descripción, módulo, tipo e ID del documento y líneas por código de cuenta.
- La empresa se obtiene del `ITenantContext` confiable del servidor, no de un TenantId editable en el cuerpo.

## Operaciones del ejemplo de #12 y #17

| Origen | Debe | Haber | Importe del ejemplo |
|---|---|---|---|
| Compra al contado | Inventario | Caja | 30 |
| Venta al contado | Caja | Ventas | 20 |
| Costo de lo vendido | Costo de ventas | Inventario | 12 |

Son datos didácticos sin impuestos, descuentos ni crédito. Los códigos reales se resuelven con el plan
de cada empresa; no se fijan IDs ni códigos universales desde Compras o Ventas.
Propuesta: Compras solicita su asiento; Ventas solicita el ingreso y el costo calculado por Inventario.
Confirmar con el equipo si ambos registros de venta comparten un asiento o usan claves independientes.

## Validaciones y reintentos propuestos

1. Cada línea tiene debe o haber positivo, nunca ambos ni importes negativos. Dinero con `decimal`;
   confirmar la escala y el redondeo antes de consolidar la política provisional de dos decimales.
2. Al menos dos líneas; suma del debe igual a suma del haber y mayor que cero.
3. Cuentas existentes de la empresa activa, habilitadas y que permitan movimientos.
4. Fecha dentro de un período permitido según la política que acuerde el equipo.
5. Clave estable por empresa y operación, por ejemplo `purchases:receipt:<id>` o `sales:invoice:<id>`.
   El costo necesita una clave distinguible si se contabiliza por separado, por ejemplo `sales:cost:<id>`.
6. Índice único por empresa y clave. Un reintento idéntico devuelve el asiento existente con
   `AlreadyProcessed = true`; no vuelve a insertar líneas.
7. Propuesta para una clave repetida con contenido distinto: conflicto, sin modificar el asiento original.

## Fallos y consistencia

- Un asiento desbalanceado o con cuentas inválidas se rechaza con error de dominio/422 y un código estable.
- Compra/venta, stock y contabilidad deben participar en la transacción del caso de uso existente.
  Confirmar la forma de compartir conexión y transacción entre DbContexts antes de implementarlo;
  llamar a `PostAsync` por sí solo no garantiza atomicidad entre módulos.
- Si falla una parte, no se confirma parcialmente la operación. Si se pierde la respuesta, consultar/reintentar
  con la misma clave, en lugar de generar un nuevo documento.
- El SRI es externo: su recuperación se coordina por estados; no se presenta como una transacción PostgreSQL.

## Decisiones que hay que cerrar con Noe y el equipo

- Confirmar que la primera entrega está revisada e integrada antes de iniciar ACC-03.
- Acordar códigos o mecanismo de configuración de las cuatro cuentas por empresa.
- Decidir uno o dos asientos para una venta y quién entrega a Ventas el costo de Inventario.
- Acordar transacción compartida, errores, escala/redondeo, período abierto y reversos.
- Acordar turno de migración de asientos y revisión del contrato por productores y consumidores.

Después de aprobar el acuerdo, actualizar `docs/contratos-modulos.md` y las casillas de #12.
No se marca #12 ni #17 como completado con esta propuesta.
