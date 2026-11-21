# ACUERDO-03 — nivel de facturación electrónica

**Fecha:** 2026-11-20  
**Participantes:** Reyes (seguridad/identidad) y Casas (ventas/comprobantes)  
**Nivel acordado para esta entrega:** **Simulación local**

La simulación identifica el recorrido completo del ERP, pero no representa un comprobante autorizado por el SRI.
No se habilita envío a servicios reales ni se declara producción.

## Decisión

| Nivel | Estado |
|---|---|
| Simulación | **Acordado e implementado como alcance documental de esta entrega** |
| Ambiente de pruebas del SRI | Pendiente de certificado, XSD, clave de acceso y endpoints verificados |
| Producción | Bloqueada hasta acreditar pruebas y aprobar habilitación por el titular |

La elección cumple la separación del proyecto:

- Ventas calcula totales y conserva el estado comercial.
- Tributario conserva los contratos de clave de acceso, XML, firma y gateway.
- Reyes controla configuración y secretos de certificados.
- Casas integra el estado de emisión con la venta.
- Ninguna respuesta simulada se presenta como autorización tributaria.

## Qué implementa esta entrega

1. Define formalmente que el nivel inicial es simulación.
2. Conserva el estado interno de comprobantes:
   `Borrador`, `ListoParaEnvio`, `Enviado`, `Recibido`, `Devuelto`,
   `Pendiente`, `Autorizado`, `Rechazado`.
3. Mantiene `ElectronicStatus = NotRequested` hasta que exista una solicitud real o simulada
   registrada por el flujo acordado.
4. Establece que solo una respuesta verificable puede cambiar el estado a `Autorizado`.
5. Identifica como sintéticos los datos de IVA, certificados y claves usados por pruebas.
6. Define que un reintento consultará o reconciliará el resultado antes de reenviar.

No se añade un adaptador real en esta entrega: las interfaces existentes siguen siendo contratos
sin implementaciones que emitan o autoricen comprobantes.

## Requisitos para avanzar al ambiente de pruebas

Antes de conectar el servicio oficial se deben disponer y verificar los siguientes elementos:

| Requisito | Responsabilidad | Criterio de entrada |
|---|---|---|
| Certificado de firma | Reyes | Certificado válido, referencia secreta por tenant, nunca en Git ni logs |
| Esquemas XSD | Casas/Reyes | Versión vigente descargada y su hash/versión registrados |
| Clave de acceso | Casas | Algoritmo oficial de 49 dígitos, módulo 11 verificado y pruebas de vector |
| Recepción de comprobantes | Reyes | WSDL/endpoint real del ambiente de pruebas verificado |
| Autorización de comprobantes | Reyes | WSDL/endpoint real del ambiente de pruebas verificado |
| Credenciales de acceso | Reyes | Configuración fuera del repositorio y por ambiente |
| Casos de respuesta | Casas/Reyes | Recibido, Devuelto, Pendiente, Autorizado y Rechazado probados |

## Qué queda para la siguiente entrega

- Descargar y fijar la ficha técnica vigente usada.
- Implementar el XML de factura contra el XSD confirmado.
- Implementar acceso seguro al certificado PKCS#12.
- Implementar el generador oficial de clave de acceso.
- Crear adaptador para recepción y consulta de autorización del ambiente de pruebas.
- Registrar clave, ambiente, XML firmado, respuestas y correlación en la venta.
- Consultar autorización ante respuestas inciertas; nunca duplicar el envío.
- Ejecutar pruebas reproducibles sin validez tributaria.
- Mantener producción deshabilitada.

## Criterios de aceptación de esta entrega

- Existe una decisión escrita con alcance, responsables y límites.
- Se distingue claramente simulación, pruebas y producción.
- No se promete autorización real ni se usa el PDF/RIDE como sustituto.
- Se declara qué falta para pruebas.
- Se conserva una ruta técnica separada entre cálculo comercial, XML/firma y gateway.
- Los estados visibles se explican sin exponer certificados, claves ni detalles técnicos al usuario.

## Fuentes oficiales consultadas

- Portal SRI, [Facturación Electrónica](https://www.sri.gob.ec/facturacion-electronica).
- Ficha técnica enlazada por el portal: Comprobantes Electrónicos, esquema off-line.

La versión exacta de ficha/XSD y los endpoints concretos deben fijarse en una revisión posterior;
consultar el portal sin completar ese dato no habilita la emisión.
