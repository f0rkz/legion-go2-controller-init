[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\LegionGo2ControllerBridge'),
    [switch]$KeepLogs
)

$ErrorActionPreference = 'Stop'
$taskName = 'Legion Go 2 Controller Bridge'
$installedExecutable = Join-Path $InstallDirectory 'LegionGo2ControllerBridge.exe'

if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

Get-CimInstance Win32_Process -Filter "Name='LegionGo2ControllerBridge.exe'" -ErrorAction SilentlyContinue |
    Where-Object ExecutablePath -EQ $installedExecutable |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

if (Test-Path $InstallDirectory) {
    if ($KeepLogs) {
        Get-ChildItem $InstallDirectory -File |
            Where-Object Extension -NE '.log' |
            Remove-Item -Force
    }
    elseif ($PSCmdlet.ShouldProcess($InstallDirectory, 'Remove application and logs')) {
        Remove-Item $InstallDirectory -Recurse -Force
    }
}

Write-Output 'Legion Go 2 Controller Bridge uninstalled.'
