param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    dotnet restore tests/QrGuard.Contracts/QrGuard.Contracts.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Contract dependency restore failed.' }
    dotnet build tests/QrGuard.Contracts/QrGuard.Contracts.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Contract build failed.' }
    dotnet run --project tests/QrGuard.Contracts -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Contract checks failed.' }
    dotnet restore src/QrGuard.Windows/QrGuard.Windows.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Windows dependency restore failed.' }
    dotnet build src/QrGuard.Windows/QrGuard.Windows.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
    if (!$BuildOnly) { dotnet run --project src/QrGuard.Windows -c Release --no-build --no-restore }
} finally { Pop-Location }
