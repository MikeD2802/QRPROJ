param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '../lab-local/environment.json'),
    [ValidatePattern('^$|^[a-fA-F0-9]{40}$')][string]$ExpectedCommit = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sourceCommit = git -C $projectRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the tested source commit.' }
if ($ExpectedCommit -and $sourceCommit -ne $ExpectedCommit) { throw 'Checkout does not match ExpectedCommit.' }
$changes = git -C $projectRoot status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Use a clean checkout for exact-commit acceptance evidence.' }
$binaryPaths = @(
    'src/QrGuard.Windows/bin/Release/net10.0-windows10.0.19041.0/win-x64/QrGuard.Windows.dll',
    'tests/QrGuard.Windows.Contracts/bin/Release/net10.0-windows/QrGuard.Windows.Contracts.dll'
)
$binaryHashes = foreach ($relativePath in $binaryPaths) {
    $binaryPath = Join-Path $projectRoot $relativePath
    [ordered]@{
        file = $relativePath
        sha256 = if (Test-Path $binaryPath) { (Get-FileHash -Algorithm SHA256 $binaryPath).Hash.ToLowerInvariant() } else { $null }
    }
}
$system = Get-CimInstance Win32_ComputerSystem
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors
$gpu = Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion
$report = [ordered]@{
    schema_version = 2
    phase = 'P0'
    recorded_at_utc = [DateTime]::UtcNow.ToString('o')
    source_commit = $sourceCommit
    source_tree_clean = $true
    binary_hashes = @($binaryHashes)
    os_caption = $os.Caption
    os_build = $os.BuildNumber
    manufacturer = $system.Manufacturer
    model = $system.Model
    memory_bytes = $system.TotalPhysicalMemory
    cpu = @($cpu)
    gpu = @($gpu)
    power_plan = (powercfg /getactivescheme)
    manual_fields_required = @('selected display resolution', 'DPI percentage', 'phone model and camera app', 'Teams/Zoom versions', 'workload/trial counts and evidence references')
}
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath -Parent) | Out-Null
$report | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $OutputPath
Write-Output "Environment report saved to $OutputPath. Add the manual fields before recording acceptance."
