# Autenticación real (PLAT-01)

Responsable: Reyes · Revisa: Noelia Mendoza.
Inicio de sesión con **ASP.NET Core Identity + OpenIddict**, flujo **Authorization Code + PKCE** y **navegador del sistema**.
Sustituye a la cabecera de desarrollo `X-Dev-User` en el escritorio ([ADR-0004](../adr/0004-oidc-identity-openiddict.md)).

## Cómo funciona

```text
Avalonia (cliente público, sin secreto)              ERP.Api (emisor OIDC + API)                    PostgreSQL
───────────────────────────────────────              ─────────────────────────────                  ──────────
1. "Iniciar sesión" ─ abre el navegador ──────────▶  /connect/authorize (PKCE S256 obligatorio)
                                                     └▶ /account/login (correo + contraseña, Identity)  identity.users
2. Servidor local http://localhost:7890 ◀── código ─ redirección de bucle local (RFC 8252)
3. Canje código + code_verifier ─────────────────▶  /connect/token  → access (15 min) + refresh (8 h)  identity.oidc_*
4. /api/v1/... con Authorization: Bearer ────────▶  Validación local del token (firma, cifrado,
                                                     emisor, audiencia, expiración) → claim sub
5. X-Tenant-Id (empresa elegida) ────────────────▶  TenantResolutionMiddleware: membresía + permisos   platform.* (RLS)
```

- **El emisor es la propia API** (`https://localhost:7180` en desarrollo). No hay un servidor aparte que arrancar.
- **La contraseña solo la ve el navegador**; la aplicación de escritorio nunca la recibe.
- **Tokens solo en memoria** del escritorio (`OidcAuthenticationService`): al cerrar la aplicación hay que volver a entrar.
  No se escriben en registros, archivos ni Git. Por eso el cliente OpenIddict usa `DisableTokenStorage()` (no hay base
  local); el `state` del flujo va firmado y cifrado con claves efímeras del proceso.
- **`X-Tenant-Id` no autoriza por sí solo**: con un token válido, empresa ajena → 403; sin token → 401.
- **Cada inicio pide credenciales**: la cookie de la página de inicio se borra al emitir el código. Por eso «Cerrar sesión»
  en la aplicación no deja una sesión reutilizable en el navegador (no hace falta *logout* del proveedor).

## Archivos

| Parte | Ruta |
|---|---|
| Contratos de sesión del escritorio | `src/Frontend/ERP.Frontend.Shared/Session/AuthenticationContracts.cs` |
| Sesión OIDC del escritorio (tokens, renovación, expiración) | `src/Frontend/ERP.Frontend.Shared/Session/Oidc/` |
| Token Bearer + empresa en cada solicitud | `src/Frontend/ERP.Frontend.Shared/Api/ErpApiClient.cs` (`SessionHeadersHandler`) |
| Pantalla de acceso | `src/Frontend/ERP.Frontend/Modules/Platform/` (`LoginViewModel`, `LoginView.axaml`) |
| Emisor, esquemas y validación | `src/Backend/ERP.Api/Security/AuthenticationSetup.cs` |
| Endpoints OIDC y página de inicio | `src/Backend/ERP.Api/Security/Oidc/` |
| Credenciales (Identity) y almacén OpenIddict | `src/Backend/ERP.Modules.Platform/Data/AuthDbContext.cs`, esquema `identity` |
| Semilla de credenciales demo | `src/Backend/ERP.DbMigrator/DevelopmentSeeder.cs` (`Seed:DemoPassword`) |
| Pruebas | `tests/ERP.IntegrationTests/Api/OidcAuthenticationApiTests.cs`, `tests/ERP.UnitTests/Frontend/OidcAuthenticationServiceTests.cs` |

## Endpoints (no requieren token)

| Método | Ruta | Uso |
|---|---|---|
| GET | `/.well-known/openid-configuration` | Descubrimiento (lo usa el cliente OpenIddict) |
| GET | `/connect/authorize` | Autorización; sin cookie de inicio redirige a `/account/login` |
| POST | `/connect/token` | Canje del código (con `code_verifier`) y renovación |
| POST | `/connect/revoke` | Revocación del token de renovación al cerrar sesión |
| GET/POST | `/account/login` | Página de correo y contraseña (antifalsificación, bloqueo tras 5 intentos durante 5 min) |

Cliente registrado al arrancar la API (`OidcClientRegistrar`): `erp-desktop`, **público** (sin `client_secret`), nativo,
solo `authorization_code` + `refresh_token`, PKCE obligatorio, redirección `http://localhost:7890/callback/login/local`.

## Guía: arrancar emisor, API y cliente

Requisitos: README pasos 1–3 (SDK 10, Docker, base local o rama Neon propia).

1. **Contraseña de los usuarios demo** (una vez; elija una de al menos 10 caracteres y no la publique):

   ```powershell
   dotnet user-secrets set "Seed:DemoPassword" "<su-clave-local>" --project src/Backend/ERP.DbMigrator
   ```

   `scripts/setup-local.ps1` la genera al azar si no existe; para verla: `dotnet user-secrets list --project src/Backend/ERP.DbMigrator`.

2. **Migraciones y semillas** (crea el esquema `identity` y las credenciales de Ana, Bruno y Carla):

   ```powershell
   dotnet run --project src/Backend/ERP.DbMigrator -- migrate seed-dev
   ```

3. **Emisor + API** (son el mismo proceso). La primera vez confíe en el certificado HTTPS de desarrollo:

   ```powershell
   dotnet dev-certs https --trust
   dotnet run --project src/Backend/ERP.Api --launch-profile https   # https://localhost:7180
   ```

   En Development OpenIddict crea sus certificados de firma y cifrado en el almacén del usuario de Windows
   (*CurrentUser\My*), no en el repositorio.

4. **Cliente de escritorio**:

   ```powershell
   dotnet run --project src/Frontend/ERP.Frontend
   ```

   «Iniciar sesión» abre el navegador → ingrese p. ej. `ana.admin@demo.local` y la contraseña del paso 1 → vuelva a la
   aplicación y elija la empresa.

Variables opcionales del escritorio: `ERP_API_BASE_URL` (emisor y API, por defecto `https://localhost:7180/`) y
`ERP_OIDC_REDIRECT_PORT` (por defecto `7890`; si lo cambia, registre la misma URL en la API con
`Authentication__Oidc__DesktopClient__RedirectUris__0=http://localhost:<puerto>/callback/login/local`).

### Probar con curl (solo Development)

`X-Dev-User` sigue disponible **solo** en Development/Testing para pruebas rápidas:
`curl.exe -H "X-Dev-User: ana.admin@demo.local" https://localhost:7180/api/v1/me`.
Fuera de esos entornos la API se niega a arrancar si alguien lo habilita, y el escritorio ya no lo envía.

## Comportamiento

| Situación | Resultado |
|---|---|
| Credenciales correctas | Código → token → `/api/v1/me` → selección de empresa |
| Contraseña o usuario incorrecto | Mensaje genérico «Correo o contraseña incorrectos.» en el navegador; se puede reintentar |
| 5 intentos fallidos | Cuenta bloqueada 5 min (Identity) |
| «Cancelar» en el navegador | El escritorio recibe `access_denied` → «Inicio de sesión cancelado.» |
| «Cancelar» en la aplicación o 5 min sin respuesta | Deja de esperar; no hay sesión |
| Token a menos de 60 s de vencer | Se renueva **una vez** (una sola renovación aunque haya solicitudes simultáneas) |
| Renovación rechazada o API responde 401 | La sesión termina **una vez** → pantalla de inicio con «Su sesión expiró»; sin reintentos ni bucles |
| Cerrar sesión | Se descartan los tokens y se revoca el de renovación (mejor esfuerzo, 5 s) |
| Cuenta desactivada en `platform.users` | No puede iniciar sesión ni renovar; un token ya emitido vale hasta 15 min |

## Configuración de producción

Sección `Authentication:Oidc` (variables `Authentication__Oidc__...`), nunca en archivos versionados:

- `SigningCertificate:Path` / `:Password` y `EncryptionCertificate:Path` / `:Password`: certificados `.pfx`. Sin ellos la API
  no arranca fuera de Development/Testing.
- `AccessTokenLifetime` (por defecto `00:15:00`), `RefreshTokenLifetime` (`08:00:00`).
- `DesktopClient:RedirectUris`: redirecciones de bucle local permitidas.

El emisor exige HTTPS (no hay excepción configurada para producción). En pruebas automatizadas se usan claves efímeras.

## Pruebas

```powershell
dotnet test --project tests/ERP.UnitTests          # sesión del escritorio: cancelación, renovación, expiración, 401
dotnet test --project tests/ERP.IntegrationTests   # requiere Docker: flujo OIDC completo contra PostgreSQL real
```

Las de integración hacen el papel del navegador (cookies y antifalsificación reales) y cubren: login con PKCE, credenciales
rechazadas, usuario inexistente, cancelación, antifalsificación, `returnUrl` externo, autorización sin PKCE, redirección no
registrada, `code_verifier` incorrecto, nueva solicitud de credenciales, renovación, 401 (sin token, inválido, alterado,
expirado), 403 (empresa ajena, sin permiso), aislamiento A/B y `X-Dev-User` deshabilitado o fuera de Development.

## Limitaciones conocidas

- El inicio de sesión real con el escritorio Avalonia y el navegador del sistema se verificó manualmente en Windows 11;
  macOS y Linux no se probaron.
- Si el puerto 7890 está ocupado, el escritorio no puede recibir la respuesta: cambie `ERP_OIDC_REDIRECT_PORT` (ver arriba).
- No hay pantalla para cambiar o recuperar la contraseña, ni administración de usuarios (PLAT-02).
- Un token de acceso ya emitido sigue siendo válido hasta su expiración (15 min) aunque se revoque la sesión o se desactive
  la cuenta; la renovación sí se bloquea.
- La tabla `identity.users` no tiene RLS (identidad global); el rol `erp_app` la lee para validar contraseñas.
