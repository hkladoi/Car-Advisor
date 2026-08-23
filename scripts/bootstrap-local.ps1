$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $root ".env"
$example = Join-Path $root ".env.example"

if (-not (Test-Path -LiteralPath $envFile)) {
    Copy-Item -LiteralPath $example -Destination $envFile
    Write-Host "Created .env from .env.example. Review local credentials before sharing the machine."
}

docker compose --project-directory $root up --build -d
docker compose --project-directory $root ps

