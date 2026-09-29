[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\LegionGo2ControllerBridge')
)

$ErrorActionPreference = 'Stop'
$taskName = 'Legion Go 2 Controller Bridge'
$sourceDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExecutable = Join-Path $sourceDirectory 'LegionGo2ControllerBridge.exe'
$sourceIcon = Join-Path $sourceDirectory 'controller-bridge.ico'
$installedExecutable = Join-Path $InstallDirectory 'LegionGo2ControllerBridge.exe'

if (-not (Test-Path $sourceExecutable -PathType Leaf)) {
    throw "LegionGo2ControllerBridge.exe was not found beside Install.ps1. Extract the complete release archive first."
}

$legionSpaceRoot = Join-Path $env:ProgramFiles 'Lenovo\LegionSpace'
$controllerDll = Get-ChildItem $legionSpaceRoot -Filter 'LEGOKZHandle.dll' -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object FullName -Like '*\SapientiaUsb\LEGOKZHandle.dll' |
    Select-Object -First 1
$settingDll = Get-ChildItem $legionSpaceRoot -Filter 'LegionSetting.dll' -File -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1

if (-not $controllerDll -or -not $settingDll) {
    throw "A supported Legion Space installation was not found under $legionSpaceRoot."
}

$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existingTask -and $existingTask.State -eq 'Running') {
    Stop-ScheduledTask -TaskName $taskName
}

Get-CimInstance Win32_Process -Filter "Name='LegionGo2ControllerBridge.exe'" -ErrorAction SilentlyContinue |
    Where-Object ExecutablePath -EQ $installedExecutable |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

New-Item $InstallDirectory -ItemType Directory -Force | Out-Null
Copy-Item $sourceExecutable $installedExecutable -Force
if (Test-Path $sourceIcon -PathType Leaf) {
    Copy-Item $sourceIcon (Join-Path $InstallDirectory 'controller-bridge.ico') -Force
}

$userId = "$env:USERDOMAIN\$env:USERNAME"
$action = New-ScheduledTaskAction -Execute $installedExecutable -WorkingDirectory $InstallDirectory
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
$trigger.Delay = 'PT10S'
$principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew

Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description 'Initializes the Legion Go 2 controller and repairs side-drawer shortcut routing.' `
    -Force | Out-Null

Start-ScheduledTask -TaskName $taskName
Start-Sleep -Seconds 2

$process = Get-CimInstance Win32_Process -Filter "Name='LegionGo2ControllerBridge.exe'" |
    Where-Object ExecutablePath -EQ $installedExecutable |
    Select-Object -First 1
if (-not $process) {
    throw "The application was installed, but it did not remain running. Check $(Join-Path $InstallDirectory 'tray.log')."
}

Write-Output "Installed to $InstallDirectory"
Write-Output "Scheduled task: $taskName"
Write-Output "Process ID: $($process.ProcessId)"
