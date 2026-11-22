# Facturación electrónica — diseño propuesto

> Propuesta técnica. La secuencia exacta de estados y mensajes se confirmará con la ficha técnica vigente (SRI-01).

## Separación de responsabilidades
1. **Cálculo comercial** (Ventas): precios, descuentos, totales.
2. **Cálculo tributario** (Ecuador): tarifas por código y fecha del hecho generador vía `TaxRateCatalog`.
3. **Registro contable** (Accounting vía `IAccountingPostingService`).
4. **Emisión electrónica** (submódulo SRI): XML → firma → envío → autorización.

## Flujo (transmisión inmediata, vigente desde 2026-01-01)
```text
Borrador ─▶ ListoParaEnvio ─(clave de acceso + XML + firma)─▶ Enviado ─▶ Recibido ─▶ Autorizado
                                                              │            └▶ Rechazado (no autorizado)
                                                              └▶ Devuelto (errores de recepción)
Pendiente = respuesta aún no disponible; se consulta autorización con reintentos idempotentes.
```
- La emisión se ejecuta **al confirmar** el documento (no en colas diferidas como vía normal). Los reintentos solo cubren fallos transitorios y son idempotentes por clave de acceso.
- Un documento autorizado nunca se elimina; correcciones mediante nota de crédito/débito o anulación según procedimiento vigente.
- Nunca se marca "Autorizado" sin respuesta verificable del SRI. El PDF/RIDE no sustituye al XML autorizado.

## Ambientes
- Por defecto: **simulador** (pruebas automáticas) y **ambiente de pruebas del SRI** (sin validez tributaria).
- Producción: adaptador distinto, habilitado por configuración explícita por contribuyente y solo con autorización del titular del certificado.

## ACUERDO-03 — alcance decidido

Esta entrega adopta **simulación** como nivel operativo acordado entre Reyes y Casas.
La simulación sirve para probar estados, idempotencia y recuperación visual. No firma XML real,
no usa certificados reales y no conecta ni recibe respuestas del SRI.

La siguiente entrega puede entrar al ambiente de pruebas únicamente después de registrar:
certificado de firma, versión XSD, clave de acceso, WSDL/endpoints de recepción y autorización,
creación segura de secretos y casos de respuesta.

Producción permanece deshabilitada hasta completar pruebas y obtener la habilitación correspondiente.
La decisión completa está en [ACUERDO-03](acuerdo-sri-nivel-entrega.md).

## Datos a persistir (Ventas/SRI)
Clave de acceso, ambiente, tipo, versión de esquema, XML generado/firmado/autorizado, número y fecha de autorización, mensajes, correlación, intentos.
