[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'LegionGo2ControllerBridge.csproj'
$publishDirectory = Join-Path $OutputDirectory 'publish'
$stageDirectory = Join-Path $OutputDirectory 'LegionGo2ControllerBridge-win-x64'
$archive = Join-Path $OutputDirectory 'LegionGo2ControllerBridge-win-x64.zip'

Remove-Item $publishDirectory,$stageDirectory,$archive -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

New-Item $stageDirectory -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $publishDirectory 'LegionGo2ControllerBridge.exe') $stageDirectory
Copy-Item (Join-Path $PSScriptRoot 'assets\controller-bridge.ico') $stageDirectory
Copy-Item (Join-Path $PSScriptRoot 'Install.ps1') $stageDirectory
Copy-Item (Join-Path $PSScriptRoot 'Uninstall.ps1') $stageDirectory
Copy-Item (Join-Path $PSScriptRoot 'README.md') $stageDirectory
Copy-Item (Join-Path $PSScriptRoot 'LENOVO_REPORT.md') $stageDirectory
Copy-Item (Join-Path $PSScriptRoot 'LICENSE') $stageDirectory
Copy-Item (Join-Path $PSScriptRoot 'SUPPORT.md') $stageDirectory
Copy-Item (Join-Path $PSScriptRoot 'KNOWN_ISSUES.md') $stageDirectory
Copy-Item (Join-Path $PSScriptRoot 'Collect-Diagnostics.ps1') $stageDirectory

Compress-Archive -Path (Join-Path $stageDirectory '*') -DestinationPath $archive
$hash = Get-FileHash $archive -Algorithm SHA256
$hashLine = "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($archive))"
Set-Content -Path "$archive.sha256" -Value $hashLine -Encoding ascii

Write-Output $archive
Write-Output "$archive.sha256"
