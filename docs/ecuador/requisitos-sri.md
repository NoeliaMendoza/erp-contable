# Requisitos SRI — alcance y estado

## Implementado (sin pretensión de cumplimiento)
- Separación tenant / contribuyente (RUC) / establecimiento / punto de emisión / sucursal (`platform`).
- Validación **sintáctica** de RUC y cédula (`ERP.Tributario.Identification`). No consulta el estado real del contribuyente en el SRI.
- Atributos tributarios del contribuyente (obligado a llevar contabilidad, contribuyente especial, agente de retención, régimen, vigencia) — datos declarados, a verificar.
- Catálogo tributario versionado por vigencia (`TaxRateCatalog`); sin tarifas oficiales cargadas.
- Contratos del submódulo SRI (`ERP.Tributario.Sri`): estados, ambientes, generador de clave de acceso, constructor XML, firmador, gateway.

## Pendiente
- Lectura de la ficha técnica v2.34 y fijación de la versión de XSD por comprobante (SRI-01).
- Adaptador simulador, adaptador del ambiente de pruebas, adaptador de producción (este último solo con autorización explícita del titular del certificado).
- Gestión segura del certificado PKCS#12 (almacén de secretos por contribuyente; nunca en repositorio ni en Avalonia).
- Transmisión inmediata e idempotente por clave de acceso; registro de mensajes y correlación.
- Conservación de XML firmado/autorizado y RIDE.
- Motor de retenciones, reportes de conciliación (ventas, compras, IVA, retenciones), ATS.

Fuentes y estado detallado: [matriz-normativa.md](matriz-normativa.md).
