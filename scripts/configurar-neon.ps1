<#
.SYNOPSIS
    Conecta su entorno a SU rama personal de Neon (dev-<nombre>), guardando las credenciales en User Secrets.

.DESCRIPTION
    - Pide las contraseñas de erp_migrator y erp_app de forma oculta (no quedan en el historial).
    - Guarda dos cadenas: migraciones por el endpoint DIRECTO y la API por el endpoint con POOLER.
    - Se niega a configurar las ramas compartidas `production` y `development`.
    - Con -Migrate aplica las migraciones (y datos ficticios) en su rama personal.

    Requisitos previos (docs/neon.md): tener acceso al proyecto Neon y haber creado su rama dev-<nombre>
    a partir de `development`.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\configurar-neon.ps1 -Branch dev-noelia -Endpoint ep-cool-river-a1b2c3d4.us-east-2.aws.neon.tech -Migrate
#>
[CmdletBinding()]
param(
    # Nombre de SU rama en Neon. Debe empezar con "dev-".
    [Parameter(Mandatory)] [string] $Branch,

    # Host del endpoint de la rama, SIN "-pooler" (Neon → Connect → desactivar "Connection pooling").
    [Parameter(Mandatory)] [string] $Endpoint,

    [string] $Database = 'neondb',

    # Aplica migraciones y datos ficticios en la rama personal.
    [switch] $Migrate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($Branch -notmatch '^dev-[a-z0-9-]+$') {
    throw "Use solo su rama personal (dev-<nombre>, en minúsculas). Las ramas 'production' y 'development' no se configuran desde una máquina personal."
}
if ($Endpoint -notmatch '^ep-[a-z0-9-]+\..+\.neon\.tech$') {
    throw 'El endpoint debe parecerse a ep-xxxx-xxxx-123456.<region>.aws.neon.tech (sin https:// ni puerto).'
}

$directHost = $Endpoint -replace '-pooler\.', '.'
$endpointId = $directHost.Split('.')[0]
$poolerHost = $directHost -replace "^$([regex]::Escape($endpointId))\.", "$endpointId-pooler."

function Read-Secret([string] $prompt) {
    $secure = Read-Host -Prompt $prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

Write-Host "Rama: $Branch | Directo: $directHost | Pooler: $poolerHost | Base: $Database" -ForegroundColor Cyan
$migratorPassword = Read-Secret 'Contraseña de erp_migrator'
$appPassword = Read-Secret 'Contraseña de erp_app'

$common = 'SSL Mode=VerifyFull;Channel Binding=Require'
$migrator = "Host=$directHost;Database=$Database;Username=erp_migrator;Password=$migratorPassword;$common"
$runtime = "Host=$poolerHost;Database=$Database;Username=erp_app;Password=$appPassword;$common"

dotnet user-secrets set 'ConnectionStrings:ErpMigrator' $migrator --project src/Backend/ERP.DbMigrator | Out-Null
dotnet user-secrets set 'ConnectionStrings:ErpRuntime' $runtime --project src/Backend/ERP.Api | Out-Null
dotnet user-secrets remove 'ConnectionStrings:Redis' --project src/Backend/ERP.Api 2>$null | Out-Null
Write-Host 'Cadenas guardadas en User Secrets. (Redis desactivado: la API usará caché en memoria.)' -ForegroundColor Green

if ($Migrate) {
    dotnet tool restore | Out-Null
    dotnet run --project src/Backend/ERP.DbMigrator -- migrate seed-dev
    if ($LASTEXITCODE -ne 0) { throw 'Falló el migrador. Verifique endpoint, contraseñas y que la rama exista.' }
}

Write-Host "`nPara volver a la base local de Docker: powershell -ExecutionPolicy Bypass -File .\scripts\setup-local.ps1"
