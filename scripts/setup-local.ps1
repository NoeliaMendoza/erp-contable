<#
.SYNOPSIS
    Configura el entorno de desarrollo LOCAL (PostgreSQL + Redis en Docker) en un solo paso.

.DESCRIPTION
    1. Verifica .NET SDK y Docker.
    2. Crea infrastructure/.env con contraseñas aleatorias (si no existe).
    3. Levanta PostgreSQL y Redis con Docker Compose y espera a que estén listos.
    4. Guarda las cadenas de conexión en User Secrets (fuera del repositorio).
    5. Restaura herramientas, aplica migraciones y carga datos ficticios.

    Es seguro ejecutarlo varias veces: no sobrescribe el .env ni duplica datos.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\setup-local.ps1
#>
[CmdletBinding()]
param(
    # No aplica migraciones ni semillas (solo servicios y secretos).
    [switch] $SkipDatabase
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Write-Step([string] $text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

function New-RandomSecret {
    $bytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return ([BitConverter]::ToString($bytes) -replace '-', '').ToLowerInvariant()
}

function Read-EnvFile([string] $path) {
    $values = @{}
    foreach ($line in Get-Content $path) {
        if ($line -match '^\s*([A-Z_]+)\s*=\s*(.*)\s*$') { $values[$Matches[1]] = $Matches[2] }
    }
    return $values
}

Write-Step 'Verificando requisitos'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'No se encontró el SDK de .NET. Instale .NET 10 (ver README).' }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'No se encontró Docker. Instale y abra Docker Desktop (ver README).' }
docker info *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker no está en ejecución. Abra Docker Desktop y vuelva a intentar.' }
Write-Host ".NET SDK $(dotnet --version) y Docker disponibles."

Write-Step 'Archivo infrastructure/.env'
$envFile = Join-Path $root 'infrastructure\.env'
if (Test-Path $envFile) {
    Write-Host 'Ya existe; se conserva.'
}
else {
    $content = (Get-Content (Join-Path $root 'infrastructure\env.example')) `
        -replace '^POSTGRES_SUPERUSER_PASSWORD=.*', "POSTGRES_SUPERUSER_PASSWORD=$(New-RandomSecret)" `
        -replace '^ERP_MIGRATOR_PASSWORD=.*', "ERP_MIGRATOR_PASSWORD=$(New-RandomSecret)" `
        -replace '^ERP_APP_PASSWORD=.*', "ERP_APP_PASSWORD=$(New-RandomSecret)"
    [IO.File]::WriteAllLines($envFile, $content)
    Write-Host 'Creado con contraseñas aleatorias (este archivo NO se sube a Git).'
}
$cfg = Read-EnvFile $envFile
$port = if ($cfg['POSTGRES_PORT']) { $cfg['POSTGRES_PORT'] } else { '5432' }
$redisPort = if ($cfg['REDIS_PORT']) { $cfg['REDIS_PORT'] } else { '6379' }
$db = if ($cfg['ERP_DB_NAME']) { $cfg['ERP_DB_NAME'] } else { 'erp' }

Write-Step 'Levantando PostgreSQL y Redis'
docker compose -f infrastructure/docker-compose.yml --env-file $envFile up -d
if ($LASTEXITCODE -ne 0) { throw 'docker compose falló. ¿Está ocupado el puerto 5432 o 6379? Cambie POSTGRES_PORT/REDIS_PORT en infrastructure/.env.' }

$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    $status = docker compose -f infrastructure/docker-compose.yml --env-file $envFile ps --format '{{.Service}} {{.Status}}'
    if (($status -match 'postgres.*healthy') -and ($status -match 'redis.*healthy')) { $ready = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $ready) { throw 'Los servicios no quedaron listos. Revise: docker compose -f infrastructure/docker-compose.yml logs' }
Write-Host 'Servicios listos.'

Write-Step 'Guardando cadenas de conexión en User Secrets'
$migrator = "Host=localhost;Port=$port;Database=$db;Username=erp_migrator;Password=$($cfg['ERP_MIGRATOR_PASSWORD'])"
$runtime = "Host=localhost;Port=$port;Database=$db;Username=erp_app;Password=$($cfg['ERP_APP_PASSWORD'])"
dotnet user-secrets set 'ConnectionStrings:ErpMigrator' $migrator --project src/Backend/ERP.DbMigrator | Out-Null
dotnet user-secrets set 'ConnectionStrings:ErpRuntime' $runtime --project src/Backend/ERP.Api | Out-Null
dotnet user-secrets set 'ConnectionStrings:Redis' "localhost:$redisPort" --project src/Backend/ERP.Api | Out-Null
Write-Host 'Listo (se guardan en su perfil de usuario, no en el repositorio).'

if ($SkipDatabase) {
    Write-Host "`nSe omitieron migraciones y semillas (-SkipDatabase)." -ForegroundColor Yellow
    exit 0
}

Write-Step 'Aplicando migraciones y datos ficticios'
dotnet tool restore | Out-Null
dotnet run --project src/Backend/ERP.DbMigrator -- migrate seed-dev
if ($LASTEXITCODE -ne 0) { throw 'Falló el migrador. Si el error dice "Control de aplicaciones bloqueó este archivo", vea la nota de Smart App Control en el README.' }

Write-Host "`nEntorno local listo. Siguiente paso:" -ForegroundColor Green
Write-Host '  dotnet run --project src/Backend/ERP.Api --launch-profile https'
