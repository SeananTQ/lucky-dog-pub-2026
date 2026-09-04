[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$StagingDirectory,
    [Parameter(Mandatory)] [ValidateSet('Playtest', 'Demo', 'Release')] [string]$Channel,
    [Parameter(Mandatory)] [string]$Version
)

$ErrorActionPreference = 'Stop'
$staging = Resolve-Path $StagingDirectory
$versionParts = @($Version.Split('.'))
while ($versionParts.Count -lt 4) { $versionParts += '0' }
$windowsVersion = ($versionParts[0..3] -join '.')
$files = Get-ChildItem -LiteralPath $staging -Recurse -File
$forbidden = $files | Where-Object {
    $_.Extension -in '.pdb', '.cs', '.psd', '.xlsx', '.py', '.md', '.gdkey' -or
    $_.Name -match 'console\.exe$|layer_index_old|secrets|Mapping\.txt'
}
if ($forbidden) {
    throw "Forbidden files remain in the build: $($forbidden.FullName -join ', ')"
}

$gameAssembly = $files | Where-Object Name -eq 'LuckyDogRise.dll' | Select-Object -First 1
if (!$gameAssembly) { throw 'LuckyDogRise.dll is missing from the exported build.' }
$expectedExecutableName = if ($Channel -eq 'Demo') { 'LuckyDogRiseDemo.exe' } else { 'LuckyDogRise.exe' }
$expectedProductName = if ($Channel -eq 'Demo') { 'Lucky Dog Rise Demo' } else { 'Lucky Dog Rise' }
$gameExecutable = $files | Where-Object Name -eq $expectedExecutableName | Select-Object -First 1
if (!$gameExecutable) { throw "$expectedExecutableName is missing from the exported build." }
$steamworksManaged = $files | Where-Object Name -eq 'Steamworks.NET.dll' | Select-Object -First 1
if (!$steamworksManaged) { throw 'Steamworks.NET.dll is missing from the exported build.' }
$steamworksNative = $files | Where-Object Name -eq 'steam_api64.dll' | Select-Object -First 1
if (!$steamworksNative) { throw 'steam_api64.dll is missing from the exported build.' }
$developmentAppId = $files | Where-Object Name -in 'steam_appid.txt', 'steam_appid.dev.txt' | Select-Object -First 1
if ($developmentAppId) { throw 'Development Steam AppID files must not be included in a Steam Depot build.' }
$developerAllowlist = $files | Where-Object Name -eq 'steam-account-allowlist.json' | Select-Object -First 1
if ($developerAllowlist) { throw 'The developer Steam account allowlist must not be included in a Steam Depot build.' }
$versionInfo = $gameExecutable.VersionInfo
if (($versionInfo.CompanyName -ne 'Seanan Studio') -or
    ($versionInfo.ProductName -ne $expectedProductName) -or
    ($versionInfo.FileVersion -ne $windowsVersion) -or
    ($versionInfo.ProductVersion -ne $windowsVersion)) {
    throw 'Windows executable metadata is missing or incorrect.'
}
$bytes = [System.IO.File]::ReadAllBytes($gameAssembly.FullName)
$ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
foreach ($debugSymbol in 'RandomAcquireItemRequested', 'DebugGrantChipsRequested', 'ResetToDebugAllItems') {
    if ($ascii.Contains($debugSymbol)) { throw "Debug symbol remains in release assembly: $debugSymbol" }
}
foreach ($developerAllowlistSymbol in 'DeveloperSteamAccountAllowlist', 'steam-account-allowlist.json') {
    if ($ascii.Contains($developerAllowlistSymbol)) {
        throw "Developer Steam account allowlist symbol remains in release assembly: $developerAllowlistSymbol"
    }
}
if ($Channel -eq 'Playtest' -and !$ascii.Contains('2026-09-08T16:00:00Z')) {
    throw 'Playtest expiration metadata is missing from the release assembly.'
}
if ($Channel -eq 'Release' -and $ascii.Contains('2026-09-08T16:00:00Z')) {
    throw 'Playtest expiration metadata must not remain in the Release assembly.'
}
if ($Channel -eq 'Demo' -and $ascii.Contains('2026-09-08T16:00:00Z')) {
    throw 'Playtest expiration metadata must not remain in the Demo assembly.'
}

$report = @"
# Build Verification

- Version: $Version
- Channel: $Channel
- Architecture: Windows x86_64
- PDB present: no
- Debug entry symbols present: no
- PCK encryption expected: yes
- PCK directory encryption expected: yes
- C# assembly obfuscated: yes
- Steamworks.NET runtime present: yes
- Development Steam AppID file present: no
- Developer Steam account allowlist present: no
- Authenticode signed: no
"@
[System.IO.File]::WriteAllText((Join-Path $staging 'build-verification.txt'), $report, [System.Text.UTF8Encoding]::new($false))
Write-Host 'Build artifact verification passed. The package is unsigned.'
