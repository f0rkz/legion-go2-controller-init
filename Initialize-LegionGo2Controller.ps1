[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$legionSpaceRoot = Join-Path $env:ProgramFiles 'Lenovo\LegionSpace'
$controllerDll = Get-ChildItem -Path $legionSpaceRoot -Filter 'LEGOKZHandle.dll' -File -Recurse |
    Where-Object FullName -Like '*\SapientiaUsb\LEGOKZHandle.dll' |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1

if (-not $controllerDll) {
    throw "LEGOKZHandle.dll was not found under $legionSpaceRoot"
}

$escapedDllPath = $controllerDll.FullName.Replace('\', '\\')
$nativeMethods = @"
using System.Runtime.InteropServices;

public static class LegionGo2ControllerNative
{
    private const string DllPath = "$escapedDllPath";

    [DllImport(DllPath, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Init();

    [DllImport(DllPath, CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetLastErr();

    [DllImport(DllPath, CallingConvention = CallingConvention.Cdecl)]
    public static extern int getUSBMode();

    [DllImport(DllPath, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FreeSapientiaUsb();
}
"@

Add-Type -TypeDefinition $nativeMethods

$initialized = [LegionGo2ControllerNative]::Init()
$lastError = [LegionGo2ControllerNative]::GetLastErr()
$usbMode = [LegionGo2ControllerNative]::getUSBMode()

Write-Output "DLL=$($controllerDll.FullName) INIT=$initialized LASTERR=$lastError MODE=$usbMode"

if ($initialized -ne 1 -or $lastError -ne 0) {
    throw "Controller initialization failed: INIT=$initialized LASTERR=$lastError MODE=$usbMode"
}

[LegionGo2ControllerNative]::FreeSapientiaUsb()
