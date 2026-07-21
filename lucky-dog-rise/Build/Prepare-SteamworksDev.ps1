[CmdletBinding()]
param(
    [uint32]$AppId = 4972240
)

$ErrorActionPreference = 'Stop'
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$workspace = Resolve-Path (Join-Path $projectRoot '..')
$steamworksNetRoot = Join-Path $workspace '.local-build\steamworks\Steamworks.NET-Standalone_2025.163.0\Windows-x64'
$managedLibrary = Join-Path $steamworksNetRoot 'Steamworks.NET.dll'
$nativeLibrary = Join-Path $steamworksNetRoot 'steam_api64.dll'

if (!(Test-Path -LiteralPath $managedLibrary)) {
    throw "Steamworks.NET.dll is missing: $managedLibrary"
}
if (!(Test-Path -LiteralPath $nativeLibrary)) {
    throw "steam_api64.dll is missing: $nativeLibrary"
}

$appIdPath = Join-Path $projectRoot 'steam_appid.txt'
[System.IO.File]::WriteAllText($appIdPath, $AppId.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Steam development AppID prepared: $AppId"
Write-Host 'steam_appid.txt is local-only and must not be included in a Steam Depot.'
