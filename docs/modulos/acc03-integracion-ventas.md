# ACC-03 ↔ Ventas: códigos de cuenta para confirmar una venta

**Para:** Casas (Ventas, SAL-02) y Noe (integración). **De:** Romero (Contabilidad, ACC-03 / #17).
**Estado:** bloqueo conocido y abierto. ACC-03 está implementado y probado, pero **la confirmación de
ventas no funciona hasta que Ventas aplique el cambio descrito aquí**. No se ha modificado `ERP.Modules.Sales`.

## Qué cambió en Contabilidad

`AccountingPostingService` ya valida, como define ACUERDO-02, que cada línea apunte a una cuenta que:

1. exista en el plan de **la empresa activa**,
2. esté **activa**, y
3. **admita movimientos** (`allows_posting`).

`PostingLine.AccountCode` es un **código del plan** (`^[A-Z0-9._-]{1,40}$`, normalizado como en ACC-01),
no un nombre de cuenta ni un identificador universal entre empresas.

## El bloqueo

`SaleService.ConfirmAsync` arma las líneas con **nombres fijos**:

| Lo que envía Ventas hoy | Resultado con ACC-03 |
|---|---|
| `"Caja"` | rechazado |
| `"Ventas"` | rechazado |
| `"IVA por pagar"` | rechazado |
| `"Costo de ventas"` | rechazado |
| `"Inventario"` | rechazado |

Un nombre con espacios no puede normalizarse a un código válido, así que la contabilización falla con
`posting_account_not_available` (422). Incluso `"Caja"` sin espacios sería rechazado salvo que exista una
cuenta cuyo código sea literalmente `CAJA`.

**Qué queda tras el error, hoy:** la excepción sube por `SaleService.ConfirmAsync`, así que la transacción
de Ventas no se confirma y la venta **no** queda confirmada. Eso **no** equivale a revertir la operación
completa: `InventoryStockService.RegisterAsync` se llama antes de contabilizar y abre su **propia**
transacción sobre su propio contexto, de modo que el movimiento de salida y el costo promedio ya están
confirmados cuando falla el asiento. El resultado es stock descontado sin venta ni asiento. La atomicidad
entre módulos es justamente **PLAT-03 (#35)** y sigue pendiente; hasta entonces no se puede afirmar que el
fallo deje la base como estaba.

El comportamiento de Contabilidad es el acordado: no debe adivinar una cuenta por su nombre ni crear cuentas
por omisión. Lo que falta es el mapeo del lado de Ventas, y la configuración de cuentas por empresa que lo
alimenta es **#36 ([ACC-04] Cuentas por empresa, períodos y asientos manuales)**, no #17.

## Cambio mínimo que necesita Ventas

Los códigos del recorrido del acuerdo (ya usados en las pruebas de ACC-03) son:

| Operación de la venta | Código | Nombre sugerido | Tipo / naturaleza |
|---|---|---|---|
| Cobro al contado | `1.1.01` | Caja | Asset / Debit |
| Salida de inventario | `1.1.02` | Inventario | Asset / Debit |
| IVA cobrado | `2.1.01` | IVA ventas por pagar | Liability / Credit |
| Ingreso por ventas | `4.1.01` | Ventas | Income / Credit |
| Costo de la mercancía vendida | `5.1.01` | Costo de ventas | Expense / Debit |

Dos opciones, en orden de preferencia:

1. **Mapeo configurable por empresa (preferida; la configuración es #36).** #36 publica en
   `ERP.Modules.Accounting.Contracts` el contrato para consultar las cuentas configuradas de la empresa
   (Caja, Ventas, Inventario, Costo de ventas, IVA en compras, IVA por pagar, CxC, CxP) y, según su
   descripción, incluye reemplazar los nombres fijos de `SaleService.cs` por esa configuración, coordinado
   con Casas y Torres. Ventas resuelve el código desde ahí y falla con un error de negocio propio si falta,
   antes de llamar a Contabilidad. Así cada empresa puede usar su propia codificación.
2. **Constantes provisionales.** Sustituir los cinco nombres por los códigos de la tabla, documentando que
   es provisional. Sirve para desbloquear la demostración, pero obliga a que **toda** empresa use esa codificación.

En ambos casos la empresa debe tener esas cuentas creadas, activas y con movimientos habilitados; hoy el
seeder de desarrollo **no** crea plan de cuentas, así que hay que crearlas por ACC-01 (API o pantalla) antes de confirmar.

## Lo que Contabilidad no va a hacer

- No se acepta un nombre de cuenta como código: eliminaría el aislamiento por empresa y la validación del plan.
- No se crean cuentas automáticamente al contabilizar.
- No se relaja la validación para "dejar pasar" la confirmación.

## Criterio de aceptación conjunto

- Confirmar una venta con IVA deja un asiento cuadrado con las cinco líneas del acuerdo
  (caja, ventas, IVA, costo, inventario) y la clave `sales:invoice:{id:D}`.
- Reintentar la confirmación del mismo documento devuelve el mismo asiento (`AlreadyProcessed = true`).
- Si falta una cuenta del mapeo, la venta **no** queda confirmada y el error indica qué cuenta falta.
  Mientras #35 siga pendiente, ese fallo puede dejar el movimiento de stock ya registrado: conviene que
  Ventas valide el mapeo **antes** de registrar inventario.
- Documento, movimiento de stock y asiento se guardan en una sola transacción: eso sigue dependiendo de
  **PLAT-03 (#35)**, que no forma parte de #17 y hoy **no** se cumple.

## Pendientes relacionados, para no confundirlos con este bloqueo

- **PLAT-03 (#35)**: conexión y transacción compartidas entre módulos.
- **#36 ([ACC-04])**: cuentas configurables por empresa, períodos contables y asientos manuales.
  Mientras no exista el catálogo de períodos, `period_closed` está declarado pero **no** se evalúa.
- **#34 ([ACUERDO-04] Anulaciones y devoluciones)**: `ReverseAsync` sigue deshabilitado
  (`journal_reverse_pending`) hasta acordar la corrección del documento y su efecto en inventario.
