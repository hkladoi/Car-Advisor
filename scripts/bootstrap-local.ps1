[CmdletBinding()]
param(
    [switch]$SkipInstall,
    [switch]$SkipSeed
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $root ".env"
$example = Join-Path $root ".env.example"
$secretFile = Join-Path $root "docs/CODEX-SECRETS.local.md"
$tempDir = Join-Path $root ".tmp"
$venv = Join-Path $root ".venv"
$python = Join-Path $venv "Scripts/python.exe"
$bootstrapPythonCommand = "python"
$bootstrapPythonPrefix = @()

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Missing prerequisite: $Name"
    }
}

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $Command"
    }
}

function Merge-LocalSecrets {
    if (-not (Test-Path -LiteralPath $secretFile)) { return }

    $overrides = @{}
    foreach ($line in [IO.File]::ReadAllLines($secretFile)) {
        if ($line -match '^([A-Z][A-Z0-9_]*)=(.+)$') {
            $overrides[$Matches[1]] = $Matches[2]
        }
    }
    if ($overrides.Count -eq 0) { return }

    $output = [Collections.Generic.List[string]]::new()
    $seen = @{}
    foreach ($line in [IO.File]::ReadAllLines($envFile)) {
        if ($line -match '^([A-Z][A-Z0-9_]*)=') {
            $key = $Matches[1]
            if ($overrides.ContainsKey($key)) {
                $output.Add("$key=$($overrides[$key])")
                $seen[$key] = $true
                continue
            }
        }
        $output.Add($line)
    }
    foreach ($key in $overrides.Keys | Sort-Object) {
        if (-not $seen.ContainsKey($key)) {
            $output.Add("$key=$($overrides[$key])")
        }
    }
    [IO.File]::WriteAllLines($envFile, $output, [Text.UTF8Encoding]::new($false))
    Write-Host "Merged non-empty local secret settings into .env (values hidden)."
}

function Invoke-SeedCommand([string[]]$Arguments) {
    Invoke-Checked "docker" (@("compose", "--project-directory", $root, "run", "--rm", "--no-deps") + $Arguments)
}

Assert-Command "docker"
Assert-Command "node"
Assert-Command "npm"
Assert-Command "dotnet"
Invoke-Checked "docker" @("compose", "version")

if (-not (Test-Path -LiteralPath $envFile)) {
    Copy-Item -LiteralPath $example -Destination $envFile
    Write-Host "Created .env from .env.example."
}
Merge-LocalSecrets

if (-not $SkipInstall) {
    Invoke-Checked "npm" @("ci", "--prefix", $root)
    Invoke-Checked "dotnet" @("restore", (Join-Path $root "VietnamCarPlatform.sln"))
    Invoke-Checked "dotnet" @("tool", "restore", "--tool-manifest", (Join-Path $root ".config/dotnet-tools.json"))
    if (-not (Test-Path -LiteralPath $python)) {
        if (Get-Command "py" -ErrorAction SilentlyContinue) {
            & py -3.12 -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 12) else 1)" 2>$null
            if ($LASTEXITCODE -eq 0) {
                $bootstrapPythonCommand = "py"
                $bootstrapPythonPrefix = @("-3.12")
            }
        }
        if ($bootstrapPythonCommand -eq "python") {
            Assert-Command "python"
            & python -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 12) else 1)"
            if ($LASTEXITCODE -ne 0) {
                throw "Python 3.12 or newer is required (the 'python' command is older)."
            }
        }
        Invoke-Checked $bootstrapPythonCommand ($bootstrapPythonPrefix + @("-m", "venv", $venv))
    }
    Invoke-Checked $python @("-m", "pip", "install", "-r", (Join-Path $root "workers/ingestion/requirements.txt"))
}
elseif (-not (Test-Path -LiteralPath $python)) {
    throw "-SkipInstall requires an existing .venv. Run bootstrap once without -SkipInstall."
}

Invoke-Checked "docker" @("compose", "--project-directory", $root, "config", "--quiet")
Invoke-Checked "docker" @("compose", "--project-directory", $root, "up", "--build", "--detach", "--wait")

$schema = Get-Content -LiteralPath (Join-Path $root "scripts/verify-v1.1-schema.sql") -Raw
$schema | & docker compose --project-directory $root exec -T postgres psql -U vcp -d vietnam_car_platform
if ($LASTEXITCODE -ne 0) { throw "V1.1 schema verification failed." }

New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$discoveryResult = Join-Path $tempDir "v2.1-discovery.json"
$discoveryJson = & docker compose --project-directory $root run --rm --no-deps ingestion-worker python -m ingestion.cli discover-source --registry /app/data/source-registry.v1.json --templates /app/data/discovery-query-templates.v2.json --brand Toyota --data-type price
if ($LASTEXITCODE -ne 0) { throw "V2.1 discovery smoke failed." }
[IO.File]::WriteAllLines($discoveryResult, $discoveryJson, [Text.UTF8Encoding]::new($false))
Invoke-Checked $python @((Join-Path $root "scripts/verify_v2_1_discovery.py"), $discoveryResult)
Invoke-SeedCommand @("ingestion-worker", "python", "-m", "ingestion.cli", "validate-parser-registry", "--registry", "/app/data/source-registry.v1.json", "--parsers", "/app/data/parser-registry.v2.json")

if (-not $SkipSeed) {
    $tempMount = "${tempDir}:/app/.tmp"
    $readOnlyTempMount = "${tempDir}:/app/.tmp:ro"
    $registry = "/app/data/source-registry.v1.json"
    $dsn = "host=/var/run/postgresql dbname=vietnam_car_platform user=vcp"

    Invoke-SeedCommand @("ingestion-worker", "python", "-m", "ingestion.cli", "validate-seed", "--registry", $registry, "--seed", "/app/data/seed/v1.2-initial-vehicles.json")
    Invoke-SeedCommand @("--volume", $tempMount, "ingestion-worker", "python", "-m", "ingestion.cli", "fetch-seed", "--registry", $registry, "--seed", "/app/data/seed/v1.2-initial-vehicles.json", "--manifest", "/app/.tmp/v1.2-snapshots.json")
    Invoke-SeedCommand @("--volume", $readOnlyTempMount, "ingestion-worker", "python", "-m", "ingestion.cli", "publish-seed", "--registry", $registry, "--seed", "/app/data/seed/v1.2-initial-vehicles.json", "--manifest", "/app/.tmp/v1.2-snapshots.json", "--dsn", $dsn)

    Invoke-SeedCommand @("ingestion-worker", "python", "-m", "ingestion.cli", "validate-registration-seed", "--registry", $registry, "--seed", "/app/data/seed/v1.5-registration-rules.json")
    Invoke-SeedCommand @("--volume", $tempMount, "ingestion-worker", "python", "-m", "ingestion.cli", "fetch-registration-seed", "--registry", $registry, "--seed", "/app/data/seed/v1.5-registration-rules.json", "--manifest", "/app/.tmp/v1.5-registration.json")
    Invoke-SeedCommand @("--volume", $readOnlyTempMount, "ingestion-worker", "python", "-m", "ingestion.cli", "publish-registration-seed", "--registry", $registry, "--seed", "/app/data/seed/v1.5-registration-rules.json", "--manifest", "/app/.tmp/v1.5-registration.json", "--dsn", $dsn)

    Invoke-SeedCommand @("ingestion-worker", "python", "-m", "ingestion.cli", "validate-energy-seed", "--registry", $registry, "--seed", "/app/data/seed/v1.6-energy.json")
    Invoke-SeedCommand @("--volume", $tempMount, "ingestion-worker", "python", "-m", "ingestion.cli", "fetch-energy-seed", "--registry", $registry, "--seed", "/app/data/seed/v1.6-energy.json", "--manifest", "/app/.tmp/v1.6-energy.json")
    Invoke-SeedCommand @("--volume", $readOnlyTempMount, "ingestion-worker", "python", "-m", "ingestion.cli", "publish-energy-seed", "--registry", $registry, "--seed", "/app/data/seed/v1.6-energy.json", "--manifest", "/app/.tmp/v1.6-energy.json", "--dsn", $dsn)

    Invoke-SeedCommand @("--volume", $tempMount, "ingestion-worker", "python", "-m", "ingestion.cli", "fetch-real-world-consumption", "--registry", $registry, "--manifest", "/app/.tmp/v3.3-real-world.json")
    Invoke-SeedCommand @("--volume", $readOnlyTempMount, "ingestion-worker", "python", "-m", "ingestion.cli", "publish-real-world-consumption", "--registry", $registry, "--manifest", "/app/.tmp/v3.3-real-world.json", "--dsn", $dsn)

    foreach ($verification in @(
        "verify_v1_3_catalog.py", "verify_v1_4_web.py", "verify_v1_5_onroad.py",
        "verify_v1_6_energy.py", "verify_v1_7_affordability.py",
        "verify_v1_8_financing.py", "verify_v1_9_compare.py",
        "verify_v1_10_admin.py", "verify_v1_final.py",
        "verify_v3_3_real_world.py")) {
        Invoke-Checked $python @((Join-Path $root "scripts/$verification"))
    }
}

Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:8080/health/live" | Out-Null
Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:8080/health/ready" | Out-Null
Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:3000" | Out-Null
Invoke-Checked "docker" @("compose", "--project-directory", $root, "ps")
if ($SkipSeed) {
    Write-Host "Bootstrap complete: migrations and health checks passed (official seed refresh skipped)."
}
else {
    Write-Host "Bootstrap complete: migrations, official data and health checks passed."
}
