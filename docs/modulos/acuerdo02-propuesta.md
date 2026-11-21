# ACUERDO-02 (#12): definición para revisión de Compras y Ventas

Responsable: Romero (`joel1251`). Rama de la ampliación: `feature/accounting-acuerdo-02-iva`.
Fecha: 20 de noviembre de 2026.

**Estado: base aprobada por Noe e integrada en el PR #25; ampliación de IVA pendiente de revisión.**
Se verificó la aprobación formal de Noe y la integración de #25. La ampliación incluye el IVA solicitado en
el mensaje compartido por Romero, sin atribuir aprobación de este agregado ni aceptación a otros consumidores.
El contrato detallado está en [../contratos-modulos.md](../contratos-modulos.md#accountingcontracts--acuerdo-02-12).
Las firmas de `IAccountingPostingService`, `PostingRequest`, `PostingLine` y `PostingResult` se conservan;
solo se amplía su documentación XML. No se implementa el servicio de ACC-03 en esta entrega.

## Verificación de la asignación

- Noe aprobó e integró el PR #24 de ACC-01 y cerró la tarea #6.
- Noe aprobó e integró el PR #25 con la definición base; el IVA se entrega como ampliación de esa versión.
- #12 permanece abierta y está asignada a Romero. No contiene comentarios nuevos con otras condiciones.
- #17 permanece abierta y depende de este acuerdo.
- En la consulta del tablero/tareas, Clientes #7 y Proveedores #8 todavía están abiertos.
  La preparación de este contrato no declara integrada la primera entrega completa ni habilita #17.

## Definiciones preparadas para cada criterio de #12

| Criterio | Definición propuesta y ubicación en el contrato principal |
|---|---|
| Empresa, fecha, origen y líneas | Empresa/actor desde ITenantContext; DateOnly, UUID del origen y PostingLine por código; sección Entrada y resultado |
| Compra, venta, costo e IVA | Base sin IVA: compra 30 y venta/costo 20/12. Con IVA: cuentas separadas de IVA compras recuperable e IVA ventas por pagar, tarifa por fecha y un solo asiento por documento |
| Idempotencia | Clave estable por empresa/documento; repetir contenido devuelve el mismo asiento; contenido distinto da 409; restricción única y tratamiento de carreras |
| Desbalance o fallo | Errores de dominio estables, 422/409 según el manejador común, rollback global y recuperación de respuesta incierta con la misma clave |
| Documentación principal | Sección ACUERDO-02 en docs/contratos-modulos.md, con solicitudes C# compilables y condiciones de aceptación |

Es una **definición del comportamiento**, no una ejecución de compra/venta ni una prueba del servicio aún inexistente.
La aprobación de Noe de la base no reemplaza la revisión del IVA y la coordinación con los consumidores.

## Decisiones concretas que revisar

1. **Agrupación de venta y costo.** Propuesta: un PostingRequest y una clave para ingreso, IVA y costo de la venta.
   Sin IVA son cuatro líneas; con IVA positivo son cinco. Los servicios registran ingreso e IVA cuando corresponda,
   sin movimiento/costo de mercancía.
2. **Cuentas por empresa.** Identificar Caja, Inventario, Ventas, Costo de ventas, IVA compras recuperable e IVA ventas por pagar mediante códigos reales del plan.
   Los códigos de las solicitudes de ejemplo son ficticios; no se impone un plan fiscal ni se crean cuentas automáticamente.
3. **Costo de salida.** Ventas consume InventoryMovementResult.TotalCost. Acordar ACUERDO-01/INV-03 con Inventario;
   no obtener costos de tablas ajenas ni sustituirlos por el precio de venta.
4. **Precisión y períodos.** Mantener inicialmente USD/decimal con dos decimales, sin redondeo silencioso del servicio.
   D-05 y el control de período abierto necesitan ratificación; el apartado de IVA propone base/redondeo por línea,
   sin resolver retenciones, declaraciones ni cierres completos.
5. **Consistencia.** La configuración actual no comparte transacciones entre DbContexts de módulos distintos.
   Coordinar esa capacidad común con el integrador antes de habilitar la confirmación de documentos en ACC-03/PUR-02/SAL-02.
6. **Reintentos y errores.** Aceptar conflicto por reutilización de clave con contenido distinto, recuperación de carreras
   y los nuevos códigos posting_account_not_available, posting_source_invalid y posting_idempotency_conflict.
7. **Correcciones.** No habilitar ReverseAsync hasta coordinar efectos en el documento y en el inventario.
8. **IVA.** Revisar fecha del hecho generador, precio con/sin impuesto, clasificación cero/exento/no objeto,
   snapshot tributario e IVA recuperable. El catálogo oficial/proveedor todavía está pendiente; no asumir una
   tarifa fija ni crédito tributario para toda compra. Una cuenta, tarifa o elegibilidad no definida bloquea confirmar.

## Ampliación de IVA

La definición completa, las tablas de asientos y el helper C# están en el apartado
**IVA de compras y ventas** de `docs/contratos-modulos.md`.

- Compra con IVA totalmente recuperable: Inventario por la base, IVA compras al debe y Caja por el total al haber.
- Venta: Caja por el cobro total; Ventas por la base e IVA ventas al haber; costo e Inventario en el mismo asiento.
- El costo del inventario no incluye el IVA recuperable. El tratamiento del IVA no recuperable debe definirse antes de
  confirmar y ser coherente con la valoración de Inventario; no se habilita crédito parcial/proporcional aquí.
- Los porcentajes se resuelven con `TaxRateCatalog` por código y fecha tributaria. La aplicación real debe comprobar
  fuente/vigencia/estado Verificado; el proveedor y catálogo oficial siguen pendientes.
- Se propone redondeo explícito por línea a dos decimales y sumar los resultados; sujeto a D-05 y revisión compartida.
- El ejemplo auxiliar usa una tarifa sintética del 10%: compra 33/33, venta/costo 34/34. No es una tarifa del SRI.
- Los reintentos reutilizan los importes y el snapshot originales, sin volver a calcular con la tarifa del día.

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

Ampliación IVA: los dos bloques C# del contrato principal se extrajeron y compilaron juntos contra
`Accounting.Contracts` y `ERP.Tributario`, sin errores ni advertencias. Se verificaron los ejemplos sin IVA,
IVA sintético positivo, cambio de tarifa por vigencia, IVA cero sin líneas vacías, redondeo propuesto
y rechazo de código o vigencia ausentes. Con 10% sintético: compra 33/33 y venta/costo 34/34;
con 11% sintético: 33.30/33.30 y 34.20/34.20. Estos resultados no acreditan un catálogo oficial,
la elegibilidad del crédito, la emisión de comprobantes ni el funcionamiento del servicio contable pendiente.

No se ejecutó `IAccountingPostingService`: su implementación y las pruebas de persistencia/idempotencia
corresponden a ACC-03. Los registros locales de esta revisión están en `artifacts/acuerdo02-validation` (ignorado por Git).
