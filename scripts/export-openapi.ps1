$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "apps/api/src/Api/VietnamCarPlatform.Api.csproj"
$outputDir = Join-Path $root "packages/contracts/openapi"
$output = Join-Path $outputDir "v1.json"
$work = Join-Path $root ".tmp/openapi"

New-Item -ItemType Directory -Path $outputDir,$work -Force | Out-Null
$stdout = Join-Path $work "api.out.log"
$stderr = Join-Path $work "api.err.log"
$process = Start-Process -FilePath "dotnet" -ArgumentList @("run","--project",$project,"--configuration","Release","--no-build","--urls","http://127.0.0.1:5099") -WorkingDirectory $root -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru

try {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        try {
            Invoke-WebRequest -Uri "http://127.0.0.1:5099/swagger/v1/swagger.json" -OutFile $output -UseBasicParsing
            break
        } catch {
            if ($process.HasExited) {
                throw "API exited before OpenAPI was available.`n$(Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue)"
            }
            Start-Sleep -Milliseconds 250
        }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    if (-not (Test-Path -LiteralPath $output)) {
        throw "Timed out waiting for generated OpenAPI document."
    }
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}

Write-Host "Generated $output"
