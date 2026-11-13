# Matriz normativa Ecuador

Estados: **Verificado (portal)** = consultado en el sitio oficial en la fecha indicada (aún debe leerse el documento fuente completo antes de implementar) · **Pendiente** = no verificado; no se usa ningún valor en código.

| # | Requisito / dato | Fuente oficial | Versión / fecha | Revisado | Estado | Módulo | Prueba | Responsable |
|---|---|---|---|---|---|---|---|---|
| 1 | Transmisión **inmediata** de comprobantes de venta, retención y documentos complementarios desde 2026-01-01 | [SRI noticia 1239](https://www.sri.gob.ec/detalle-noticias?idnoticia=1239&marquesina=1); Res. NAC-DGERCGC25-00000014 (30-jun-2025), reformada por NAC-DGERCGC25-00000017 (30-jul-2025) | Noticia 30-dic-2025 | 2026-11-13 | Verificado (portal) | SRI/Ventas | Diseño: sin colas diferidas como vía normal | Int. 3 |
| 2 | Ficha técnica de comprobantes electrónicos esquema off-line | [SRI facturación electrónica](https://www.sri.gob.ec/web/intersri/facturacion-electronica) | v2.34 (portal indica actualización jul-2026) | 2026-11-13 | Verificado (portal); contenido **pendiente** de lectura | SRI | SRI-01 | Int. 3 |
| 3 | XSD factura (1.0.0, 1.1.0, 2.0.0, 2.1.0), nota de crédito (1.0.0, 1.1.0), nota de débito (1.0.0), liquidación de compra (1.0.0, 1.1.0), guía de remisión (1.0.0, 1.1.0), retención (1.0.0, 2.0.0) | Ídem | Portal: feb-2022 | 2026-11-13 | Verificado (portal); versión a usar **pendiente** | SRI | SRI-02 | Int. 3 |
| 4 | Ambientes de pruebas (sin validez tributaria) y producción | Ídem | — | 2026-11-13 | Verificado (portal) | SRI | Adaptadores separados | Int. 3 |
| 5 | Endpoints de servicios web de recepción/autorización | Ficha técnica | — | — | **Pendiente** (no listados en el portal) | SRI | — | Int. 3 |
| 6 | Algoritmo de clave de acceso (49 dígitos, módulo 11) | Ficha técnica | — | — | **Pendiente** | SRI | — | Int. 3 |
| 7 | Códigos de tipo de comprobante, tipo de identificación, impuestos y porcentajes de IVA | Ficha técnica / catálogos | — | — | **Pendiente** | Ecuador/Ventas/Compras | `TaxRateCatalogTests` usa solo datos sintéticos | Int. 3/4 |
| 8 | Tarifa general de IVA vigente y tarifas diferenciadas | Normativa tributaria vigente / SRI | — | — | **Pendiente** (no codificada como constante) | Ecuador | — | Int. 3 |
| 9 | Retenciones en la fuente IR e IVA (códigos y porcentajes) | Resoluciones SRI vigentes | — | — | **Pendiente** | Compras | — | Int. 4 |
| 10 | Validación de cédula (módulo 10) y estructura de RUC | Registro Civil / SRI | — | — | **Pendiente** de fuente oficial (algoritmo de dominio público implementado) | Ecuador | `EcuadorIdValidatorTests` | Int. 3 |
| 11 | ATS y anexos | [SRI formularios e instructivos](https://www.sri.gob.ec/web/intersri/formularios-e-instructivos1) | — | — | **Pendiente** (fase posterior) | Reportes | — | Int. 6 |
| 12 | Plan de cuentas / NIIF / Superintendencia de Compañías | Supercias | — | — | **Pendiente** | Contabilidad | — | Int. 2 |
| 13 | Conservación de XML autorizados y RIDE | Normativa SRI | — | — | **Pendiente** | SRI | — | Int. 3 |
| 14 | Facturador Electrónico Gratuito disponible para descarga hasta 31-dic-2026 (referencia de mercado) | Portal SRI | — | 2026-11-13 | Verificado (portal) | — | — | — |

Nada en esta tabla implica cumplimiento u homologación. Actualizar en cada PR tributario.
