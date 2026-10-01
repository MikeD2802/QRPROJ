param([string]$OutputPath = (Join-Path $PSScriptRoot '../lab-local/environment.json'))
$ErrorActionPreference = 'Stop'
$system = Get-CimInstance Win32_ComputerSystem
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors
$gpu = Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion
$report = [ordered]@{
    schema_version = 1
    phase = 'P0'
    os_caption = $os.Caption
    os_build = $os.BuildNumber
    manufacturer = $system.Manufacturer
    model = $system.Model
    memory_bytes = $system.TotalPhysicalMemory
    cpu = @($cpu)
    gpu = @($gpu)
    power_plan = (powercfg /getactivescheme)
    manual_fields_required = @('selected display resolution', 'DPI percentage', 'phone model and camera app', 'Teams/Zoom versions', 'exact tested commit')
}
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath -Parent) | Out-Null
$report | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $OutputPath
Write-Output "Environment report saved to $OutputPath. Add the manual fields before recording acceptance."
