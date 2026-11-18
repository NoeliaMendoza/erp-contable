using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Antiforgery;

namespace ERP.Api.Security.Oidc;

/// <summary>
/// Página de inicio de sesión que ve el usuario en el navegador del sistema. HTML autocontenido (sin scripts ni recursos
/// externos), con todos los valores codificados, antifalsificación y cabeceras que impiden incrustarla o guardarla en caché.
/// </summary>
internal static class LoginPage
{
    // Codifica todo lo peligroso (<, >, &, comillas) pero deja legibles las tildes y la ñ.
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement);

    public static IResult Render(HttpContext context, IAntiforgery antiforgery, string returnUrl, string? error, string? email = null)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        var html = Html;
        var errorBlock = error is null ? string.Empty : $"""<p class="error" role="alert">{html.Encode(error)}</p>""";

        var body = $"""
            <h1>Iniciar sesión</h1>
            <p class="muted">Ingrese con su cuenta de Orvix. Al terminar puede volver a la aplicación.</p>
            {errorBlock}
            <form method="post" action="{OidcEndpoints.LoginPath}" autocomplete="on">
              <input type="hidden" name="{html.Encode(tokens.FormFieldName)}" value="{html.Encode(tokens.RequestToken ?? string.Empty)}">
              <input type="hidden" name="returnUrl" value="{html.Encode(returnUrl)}">
              <label for="email">Correo</label>
              <input id="email" name="email" type="email" autocomplete="username" value="{html.Encode(email ?? string.Empty)}" autofocus>
              <label for="password">Contraseña</label>
              <input id="password" name="password" type="password" autocomplete="current-password">
              <div class="actions">
                <button type="submit" name="action" value="cancel" class="secondary" formnovalidate>Cancelar</button>
                <button type="submit" name="action" value="login" class="primary">Ingresar</button>
              </div>
            </form>
            """;

        return Page(context, body, error is null ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
    }

    public static IResult RenderInvalidRequest(HttpContext context) => Page(context, """
        <h1>Solicitud no válida</h1>
        <p class="muted">Abra el inicio de sesión desde la aplicación de escritorio Orvix.</p>
        """, StatusCodes.Status400BadRequest);

    private static IResult Page(HttpContext context, string body, int status)
    {
        var headers = context.Response.Headers;
        headers.CacheControl = "no-store";
        headers.Pragma = "no-cache";
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        // Sin form-action: tras el envío el navegador debe poder seguir la redirección final a http://localhost (escritorio).
        headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'";

        var page = $$"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Orvix · Iniciar sesión</title>
              <style>
                :root { color-scheme: light; font-family: "Segoe UI", system-ui, sans-serif; }
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #f3f5f8; color: #1d2733; }
                main { width: min(380px, calc(100vw - 32px)); background: #fff; border-radius: 12px; padding: 28px;
                       box-shadow: 0 8px 28px rgba(16, 38, 66, .12); }
                .brand { font-weight: 700; letter-spacing: .04em; color: #0f6e8c; margin: 0 0 12px; }
                h1 { font-size: 1.35rem; margin: 0 0 6px; }
                .muted { color: #5b6878; font-size: .92rem; margin: 0 0 18px; }
                label { display: block; font-size: .9rem; margin: 12px 0 4px; }
                input[type=email], input[type=password] { width: 100%; box-sizing: border-box; padding: 10px 12px;
                       border: 1px solid #c4ccd6; border-radius: 8px; font-size: 1rem; }
                input:focus, button:focus { outline: 3px solid #8fd0e4; outline-offset: 1px; }
                .actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 22px; }
                button { border: 0; border-radius: 8px; padding: 10px 16px; font-size: .95rem; cursor: pointer; }
                .primary { background: #0f6e8c; color: #fff; }
                .secondary { background: #e7ebf0; color: #1d2733; }
                .error { background: #fdecec; color: #9b1c1c; border-radius: 8px; padding: 10px 12px; font-size: .92rem; }
              </style>
            </head>
            <body>
              <main>
                <p class="brand">ORVIX</p>
                {{body}}
              </main>
            </body>
            </html>
            """;

        return Results.Content(page, "text/html; charset=utf-8", statusCode: status);
    }
}
