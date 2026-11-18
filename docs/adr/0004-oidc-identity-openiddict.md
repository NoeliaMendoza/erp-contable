# ADR-0004: Autenticación real con Identity + OpenIddict (Authorization Code + PKCE)

- Estado: Propuesto (PLAT-01) · Fecha: 2026-11-17 · Reemplaza parcialmente a [ADR-0003](0003-autenticacion-desarrollo.md)

## Contexto
El escritorio se autenticaba con `X-Dev-User` (ADR-0003), sin contraseña y solo apto para desarrollo. La API necesita
identidad real para desplegarse, y el cliente es una aplicación instalada que no puede guardar secretos.

## Decisión
- **Emisor OIDC dentro de ERP.Api** con OpenIddict 7 (sin servidor aparte): Authorization Code con **PKCE obligatorio**,
  renovación (`offline_access`) y revocación. Tokens de acceso de 15 min con audiencia `urn:orvix:erp-api`.
- **Cliente de escritorio público y nativo** (`erp-desktop`, sin `client_secret`) con el **navegador del sistema** y
  redirección de **bucle local** `http://localhost:7890/...` (RFC 8252), usando `OpenIddict.Client.SystemIntegration`.
- **Credenciales con ASP.NET Core Identity** (solo usuarios, sin roles globales) en un `AuthDbContext` propio, esquema
  `identity`, con el **mismo Id** que `platform.users`. Los roles y permisos siguen siendo **por empresa** en `platform`.
- La API acepta solo `Authorization: Bearer` (esquema por defecto). La cookie de inicio de sesión existe únicamente para la
  página del navegador y se borra al emitir el código.
- `X-Dev-User` se conserva **solo en Development/Testing** (pruebas y curl); el escritorio deja de enviarlo.
- Tokens del escritorio **solo en memoria**.

## Alternativas descartadas
- *Resource Owner Password* (la aplicación pide la contraseña): desaconsejado por OAuth 2.1; la aplicación vería la contraseña.
- *WebView embebido*: no es el navegador del sistema (RFC 8252) y complica el soporte multiplataforma.
- *Persistir tokens cifrados (DPAPI)*: no es necesario para la primera entrega; se puede agregar detrás de `IAccessTokenProvider`.
- *Herencia de `PlatformDbContext` desde `IdentityDbContext`*: mezclaría tablas globales con el modelo multitenant y su RLS.

## Consecuencias
+ Inicio de sesión real con contraseña, bloqueo por intentos y tokens validados en la API; listo para desplegar con certificados.
+ Los módulos no cambian: siguen usando `ITenantContext` (claim `sub`) y `ErpApiClient`.
− Nuevo esquema `identity` (tablas globales sin RLS, legibles por `erp_app`) y dependencia de OpenIddict.
− Al cerrar la aplicación se pierde la sesión (tokens en memoria).
− Un token de acceso emitido vale hasta 15 min aunque la cuenta se desactive.
