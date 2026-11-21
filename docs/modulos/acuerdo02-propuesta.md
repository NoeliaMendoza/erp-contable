# ACUERDO-02 (#12): definición para revisión de Compras y Ventas

Responsable: Romero (`joel1251`). Rama: `feature/accounting-acuerdo-02`.
Fecha: 20 de noviembre de 2026.

**Estado: propuesta completa de definición, pendiente de aprobación de Noe y los consumidores.**
El contrato detallado está en [../contratos-modulos.md](../contratos-modulos.md#accountingcontracts--acuerdo-02-12).
Las firmas de `IAccountingPostingService`, `PostingRequest`, `PostingLine` y `PostingResult` se conservan;
solo se amplía su documentación XML. No se implementa el servicio de ACC-03 en esta entrega.

## Verificación de la asignación

- Noe aprobó e integró el PR #24 de ACC-01 y cerró la tarea #6.
- #12 permanece abierta y está asignada a Romero. No contiene comentarios nuevos con otras condiciones.
- #17 permanece abierta y depende de este acuerdo.
- En la consulta del tablero/tareas, Clientes #7 y Proveedores #8 todavía están abiertos.
  La preparación de este contrato no declara integrada la primera entrega completa ni habilita #17.

## Definiciones preparadas para cada criterio de #12

| Criterio | Definición propuesta y ubicación en el contrato principal |
|---|---|
| Empresa, fecha, origen y líneas | Empresa/actor desde ITenantContext; DateOnly, UUID del origen y PostingLine por código; sección Entrada y resultado |
| Compra, venta y costo | Compra: Inventario/Caja 30. Venta: Caja/Ventas 20 más Costo de ventas/Inventario 12 en un único asiento de cuatro líneas |
| Idempotencia | Clave estable por empresa/documento; repetir contenido devuelve el mismo asiento; contenido distinto da 409; restricción única y tratamiento de carreras |
| Desbalance o fallo | Errores de dominio estables, 422/409 según el manejador común, rollback global y recuperación de respuesta incierta con la misma clave |
| Documentación principal | Sección ACUERDO-02 en docs/contratos-modulos.md, con solicitudes C# compilables y condiciones de aceptación |

Es una **definición del comportamiento**, no una ejecución de compra/venta ni una prueba del servicio aún inexistente.
La aceptación de esas decisiones sigue pendiente de la revisión compartida.

## Decisiones concretas que revisar

1. **Agrupación de venta y costo.** Propuesta: un PostingRequest y una clave para las cuatro líneas de una venta de productos.
   Los servicios solo registran ingreso, sin movimiento/costo de mercancía.
2. **Cuentas por empresa.** Identificar Caja, Inventario, Ventas y Costo de ventas mediante códigos reales del plan.
   Los códigos de las solicitudes de ejemplo son ficticios; no se impone un plan fiscal ni se crean cuentas automáticamente.
3. **Costo de salida.** Ventas consume InventoryMovementResult.TotalCost. Acordar ACUERDO-01/INV-03 con Inventario;
   no obtener costos de tablas ajenas ni sustituirlos por el precio de venta.
4. **Precisión y períodos.** Mantener inicialmente USD/decimal con dos decimales, sin redondeo silencioso del servicio.
   D-05 y el control de período abierto necesitan ratificación; no se resuelven impuestos ni cierres completos aquí.
5. **Consistencia.** La configuración actual no comparte transacciones entre DbContexts de módulos distintos.
   Coordinar esa capacidad común con el integrador antes de habilitar la confirmación de documentos en ACC-03/PUR-02/SAL-02.
6. **Reintentos y errores.** Aceptar conflicto por reutilización de clave con contenido distinto, recuperación de carreras
   y los nuevos códigos posting_account_not_available, posting_source_invalid y posting_idempotency_conflict.
7. **Correcciones.** No habilitar ReverseAsync hasta coordinar efectos en el documento y en el inventario.

## Revisión y siguiente paso

Noe coordina la aceptación con los responsables actuales de Compras y Ventas. La decisión de costo también requiere
la participación de Inventario. No se atribuye aprobación a ningún integrante por haber leído o generado esta propuesta.

Después de revisar, actualizar aquí las decisiones finales, aprobar/integrar el PR y registrar el acuerdo en #12.
Solo entonces se puede considerar cerrado el acuerdo. ACC-03 requiere además la primera entrega completa integrada.
Sus pruebas futuras deben demostrar partidas balanceadas, idempotencia, aislamiento y rollback del caso de uso completo.

Este cambio no genera migraciones, no modifica módulos de compañeros, no toca main ni se conecta a Neon.

## Verificación de esta definición

- Compilación Release de la solución: correcta, sin errores ni advertencias.
- `dotnet format ERP.slnx --verify-no-changes --no-restore --severity warn`: aprobado.
- Ejemplos C# compilados contra el contrato real: compra 30/30 y venta con ingreso/costo 32/32.
- Comparación con `origin/main` excluyendo comentarios: firmas y código del contrato idénticos.

No se ejecutó `IAccountingPostingService`: su implementación y las pruebas de persistencia/idempotencia
corresponden a ACC-03. Los registros locales de esta revisión están en `artifacts/acuerdo02-validation` (ignorado por Git).
