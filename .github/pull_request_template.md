## Qué cambia

<!-- Objetivo y alcance. Enlazar el Issue: "Cierra #N" -->

Cierra #

## Módulo(s) afectado(s)

- [ ] Platform  - [ ] Accounting  - [ ] Sales/SRI  - [ ] Purchases/Treasury  - [ ] Inventory  - [ ] Reporting/UX  - [ ] Transversal (BuildingBlocks/Infrastructure/Api/CI)

## Cómo se probó

<!-- Comandos ejecutados y resultado. Capturas de Avalonia si aportan evidencia. -->

## Lista de verificación

- [ ] Ejecuté `dotnet format ERP.slnx`; compila sin advertencias y `dotnet test --solution ERP.slnx` pasa localmente.
- [ ] Sigue la [guía de estilo](../docs/guia-estilo.md) (nombres, estructura MVC, tema visual).
- [ ] Los cambios están circunscritos al módulo (o los cambios compartidos fueron acordados con los responsables).
- [ ] Acciones nuevas en `Controllers/`, heredan de `ApiControllerBase` y usan `[RequirePermission(...)]`; el tenant se toma de `ITenantContext`, nunca del cuerpo de la solicitud.
- [ ] Tablas nuevas tenant-scoped tienen `tenant_id`, política RLS (`RlsSql.EnableTenantIsolation`) y prueba negativa entre tenants.
- [ ] Si hay migración: se generó contra `main` actualizado, se revisó el SQL y se indica cómo aplicarla/revertirla.
- [ ] Si cambia un contrato (`*.Contracts`, DTO `/api/v1`), se documentó en `docs/contratos-modulos.md` y se avisó a los consumidores.
- [ ] Sin secretos, `.env`, cadenas de conexión, certificados ni datos reales.
- [ ] Valores tributarios (tarifas, códigos SRI) vienen de fuente oficial registrada en `docs/ecuador/matriz-normativa.md` o están marcados como pendientes.

## Riesgos / pendientes
