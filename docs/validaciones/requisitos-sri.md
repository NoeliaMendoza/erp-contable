# Requisitos SRI — alcance y estado

## Implementado (sin pretensión de cumplimiento)
- **Decisión ACUERDO-03:** esta entrega fija simulación como nivel acordado; producción y ambiente real quedan fuera.
- Separación tenant / contribuyente (RUC) / establecimiento / punto de emisión / sucursal (`platform`).
- Validación **sintáctica** de RUC y cédula (`ERP.Tributario.Identification`). No consulta el estado real del contribuyente en el SRI.
- Atributos tributarios del contribuyente (obligado a llevar contabilidad, contribuyente especial, agente de retención, régimen, vigencia) — datos declarados, a verificar.
- Catálogo tributario versionado por vigencia (`TaxRateCatalog`); sin tarifas oficiales cargadas.
- Contratos del submódulo SRI (`ERP.Tributario.Sri`): estados, ambientes, generador de clave de acceso, constructor XML, firmador, gateway.

## Pendiente
- Obtener el certificado de firma y el parámetro seguro por contribuyente para el ambiente de pruebas.
- Fijar la versión exacta del XSD vigente y validar un vector de factura.
- Implementar y verificar los 49 dígitos de clave de acceso y módulo 11.
- Confirmar y parametrizar los WSDL/URLs oficiales de recepción y autorización del ambiente de pruebas.
- Lectura de la ficha técnica v2.34 y fijación de la versión de XSD por comprobante (SRI-01).
- Adaptador simulador, adaptador del ambiente de pruebas, adaptador de producción (este último solo con autorización explícita del titular del certificado).
- Gestión segura del certificado PKCS#12 (almacén de secretos por contribuyente; nunca en repositorio ni en Avalonia).
- Transmisión inmediata e idempotente por clave de acceso; registro de mensajes y correlación.
- Conservación de XML firmado/autorizado y RIDE.
- Motor de retenciones, reportes de conciliación (ventas, compras, IVA, retenciones), ATS.

Fuentes y estado detallado: [matriz-normativa.md](matriz-normativa.md).
