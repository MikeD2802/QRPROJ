param([switch]$BuildOnly, [ValidatePattern('^$|^[a-fA-F0-9]{40}$')][string]$ExpectedCommit = '')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sourceCommit = git -C $projectRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
if ($ExpectedCommit -and $sourceCommit -ne $ExpectedCommit) { throw 'Checkout does not match ExpectedCommit.' }
if (!$BuildOnly) {
    $changes = git -C $projectRoot status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Use a clean checkout to record exact-commit acceptance evidence.' }
    $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $evidenceDirectory = Join-Path $projectRoot "lab-local/$sourceCommit/$runId"
    New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
    Start-Transcript -Path (Join-Path $evidenceDirectory 'run.txt') | Out-Null
}
Push-Location $projectRoot
try {
    dotnet restore tests/QrGuard.Contracts/QrGuard.Contracts.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Contract dependency restore failed.' }
    dotnet build tests/QrGuard.Contracts/QrGuard.Contracts.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Contract build failed.' }
    dotnet run --project tests/QrGuard.Contracts -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Contract checks failed.' }
    dotnet restore tests/QrGuard.P1.Contracts/QrGuard.P1.Contracts.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'P1 contract dependency restore failed.' }
    dotnet build tests/QrGuard.P1.Contracts/QrGuard.P1.Contracts.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'P1 contract build failed.' }
    dotnet run --project tests/QrGuard.P1.Contracts -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'P1 contract checks failed.' }
    dotnet restore src/QrGuard.Windows/QrGuard.Windows.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Windows dependency restore failed.' }
    dotnet build src/QrGuard.Windows/QrGuard.Windows.csproj -c Release --no-restore --no-incremental
    if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
    dotnet restore tests/QrGuard.Windows.Contracts/QrGuard.Windows.Contracts.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Windows mask contract dependency restore failed.' }
    dotnet build tests/QrGuard.Windows.Contracts/QrGuard.Windows.Contracts.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Windows mask contract build failed.' }
    if (!$BuildOnly) {
        & (Join-Path $PSScriptRoot 'collect-p0-environment.ps1') -OutputPath (Join-Path $evidenceDirectory 'environment.json') -ExpectedCommit $sourceCommit -Phase P1
        dotnet run --project tests/QrGuard.Windows.Contracts -c Release --no-build --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Windows mask API checks failed. Preserve this run and stop before capture acceptance.' }
        Write-Output "Acceptance source commit: $sourceCommit. Evidence directory: $evidenceDirectory"
        dotnet run --project src/QrGuard.Windows -c Release --no-build --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Windows lab execution failed.' }
    }
} finally {
    Pop-Location
    if (!$BuildOnly) { Stop-Transcript | Out-Null }
}
