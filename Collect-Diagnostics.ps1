[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) "LegionGo2ControllerBridge-diagnostics-$((Get-Date).ToString('yyyyMMdd-HHmmss')).zip")
)

$ErrorActionPreference = 'Stop'
$staging = Join-Path ([IO.Path]::GetTempPath()) ("LegionGo2ControllerBridge-" + [guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\LegionGo2ControllerBridge'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$taskName = 'Legion Go 2 Controller Bridge'

function Remove-LocalIdentity {
    param([AllowNull()][string]$Text)
    if ($null -eq $Text) { return $null }
    return $Text.Replace($env:USERNAME, '<user>').Replace($env:COMPUTERNAME, '<computer>')
}

New-Item $staging -ItemType Directory -Force | Out-Null
try {
    $os = Get-CimInstance Win32_OperatingSystem
    $summary = [ordered]@{
        CollectedAt = (Get-Date).ToString('o')
        OperatingSystem = $os.Caption
        Version = $os.Version
        BuildNumber = $os.BuildNumber
        Architecture = $os.OSArchitecture
        LastBoot = $os.LastBootUpTime
        PowerPlatformRole = (powercfg /GETACTIVESCHEME | Out-String).Trim()
    }

    $legionSpaceRoot = Join-Path $env:ProgramFiles 'Lenovo\LegionSpace'
    $files = Get-ChildItem $legionSpaceRoot -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object Name -In @('LEGOKZHandle.dll', 'LegionSetting.dll', 'LegionSpace.exe', 'LegionSettingMenu.exe') |
        ForEach-Object {
            [pscustomobject]@{
                RelativePath = $_.FullName.Substring($legionSpaceRoot.Length).TrimStart('\')
                FileVersion = $_.VersionInfo.FileVersion
                ProductVersion = $_.VersionInfo.ProductVersion
            }
        }

    $devices = @()
    if (Get-Command Get-PnpDevice -ErrorAction SilentlyContinue) {
        $devices = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
            Where-Object InstanceId -Like '*VID_17EF&PID_61EB*' |
            Select-Object Status,Class,FriendlyName,InstanceId
    }

    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    $taskInfo = if ($task) {
        $info = Get-ScheduledTaskInfo -TaskName $taskName
        [pscustomobject]@{
            State = $task.State.ToString()
            LastRunTime = $info.LastRunTime
            LastTaskResult = $info.LastTaskResult
            NextRunTime = $info.NextRunTime
        }
    }

    $processes = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object Name -In @('LegionGo2ControllerBridge.exe','LegionSpace.exe','LegionSettingMenu.exe','LSDaemon.exe') |
        Select-Object Name,ProcessId,CreationDate

    $since = (Get-Date).AddDays(-7)
    $events = Get-WinEvent -FilterHashtable @{ LogName = 'System'; StartTime = $since } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -eq 'Microsoft-Windows-Kernel-Power' -and $_.Id -In @(42,107,506,507) } |
        Select-Object TimeCreated,Id,ProviderName,LevelDisplayName

    $summary | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $staging 'system.json') -Encoding utf8
    $files | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $staging 'lenovo-files.json') -Encoding utf8
    $devices | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $staging 'controller-devices.json') -Encoding utf8
    $taskInfo | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $staging 'scheduled-task.json') -Encoding utf8
    $processes | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $staging 'processes.json') -Encoding utf8
    $events | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $staging 'power-events.json') -Encoding utf8

    $logPath = Join-Path $installDirectory 'tray.log'
    if (-not (Test-Path $logPath -PathType Leaf)) {
        $logPath = Join-Path $scriptDirectory 'tray.log'
    }
    if (Test-Path $logPath -PathType Leaf) {
        $redactedLog = (Get-Content $logPath -Raw) |
            ForEach-Object { Remove-LocalIdentity $_ }
        Set-Content (Join-Path $staging 'tray.log') $redactedLog -Encoding utf8
    }

    @'
Review this archive before sharing it publicly. User and computer names are
redacted from tray.log, but controller instance IDs and timestamps remain.
No Lenovo binaries, memory dumps, credentials, or general application logs are
included.
'@ | Set-Content (Join-Path $staging 'PRIVACY.txt') -Encoding utf8

    $parent = Split-Path -Parent $OutputPath
    if ($parent -and -not (Test-Path $parent)) {
        New-Item $parent -ItemType Directory -Force | Out-Null
    }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $OutputPath -Force
    Write-Output $OutputPath
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
