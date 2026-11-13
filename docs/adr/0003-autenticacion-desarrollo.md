# ADR-0003: Autenticación de desarrollo temporal

- Estado: Aceptado (temporal, se reemplaza con PLAT-01) · Fecha: 2026-11-13

## Contexto
El flujo OIDC (Identity + OpenIddict, Authorization Code + PKCE) es un entregable posterior, pero el flujo vertical necesita una identidad autenticada.

## Decisión
- La API acepta la cabecera `X-Dev-User` (correo de un usuario semilla) **solo** en los entornos Development/Testing y con `Authentication:DevelopmentHeader:Enabled=true`.
- En cualquier otro entorno la API se niega a arrancar hasta que exista un esquema de producción.

## Consecuencias
+ El equipo puede desarrollar y probar permisos y aislamiento desde el primer día.
− No apto para despliegue; PLAT-01 es prioritario.
