# ADR-0002: Pruebas con Microsoft Testing Platform y Testcontainers

- Estado: Aceptado · Fecha: 2026-11-13

## Contexto
El SDK de .NET 10 ya no admite VSTest para proyectos xUnit v3 sobre Microsoft Testing Platform, y las reglas de aislamiento solo se pueden verificar contra PostgreSQL real (RLS).

## Decisión
- xUnit v3 sobre Microsoft Testing Platform (configurado en `global.json`); se ejecuta con `dotnet test --solution ERP.slnx`.
- Pruebas de integración con PostgreSQL efímero (Testcontainers) y el rol runtime real `erp_app`.
- Reglas de arquitectura leyendo los `.csproj` y por reflexión, sin librerías adicionales.

## Consecuencias
+ Las pruebas de seguridad prueban la base real, igual en local y en CI.
− Las pruebas de integración requieren Docker en ejecución.
