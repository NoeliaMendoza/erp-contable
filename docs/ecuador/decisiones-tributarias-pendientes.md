# Decisiones tributarias pendientes

| Id | Decisión | Notas |
|---|---|---|
| T-01 | Tipos de comprobante habilitados en el MVP | ¿Solo factura y nota de crédito? ¿Retención? ¿Guía de remisión? ¿Liquidación de compra? |
| T-02 | Versión de XSD por comprobante | Portal lista varias (p. ej. factura 1.0.0–2.1.0) |
| T-03 | Regímenes tributarios cubiertos (general, RIMPE, etc.) | Define obligaciones, retenciones y comprobantes |
| T-04 | Catálogo de tarifas de IVA y sus vigencias | Cargar desde fuente oficial con `VerificationStatus.Verificado` |
| T-05 | Reglas de retención IR/IVA por tipo de agente y proveedor | Versionadas; no universales |
| T-06 | Custodia del certificado de firma | Almacén de secretos, rotación, quién lo carga |
| T-07 | Paso a producción con el SRI | Requisitos, responsable y autorización del titular |
| T-08 | Rechazar o advertir RUC con dígito módulo 11 no coincidente | Hoy: advertencia (D-15) |
| T-09 | Catálogos territoriales (provincia/cantón/parroquia) | Fuente oficial y versionado |
| T-10 | Alcance de ATS y anexos | Fase posterior; no se declara presentación automática |
| T-11 | Plan de cuentas sugerido por plantilla (comercio/servicios) y NIIF aplicable | Validar con contador |
