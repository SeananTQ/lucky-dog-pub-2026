[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ContentRoot,
    [Parameter(Mandatory)] [string]$OutputDirectory,
    [Parameter(Mandatory)] [int]$AppId,
    [Parameter(Mandatory)] [int]$DepotId,
    [Parameter(Mandatory)] [string]$Description,
    [switch]$Preview,
    [string]$SetLiveBranch
)

$ErrorActionPreference = 'Stop'
$expectedPlaytestAppId = 4972240

function ConvertTo-VdfValue([string]$Value) {
    if ($Value.Contains('"')) { throw 'SteamPipe VDF values cannot contain double quotes.' }
    return $Value.Replace('\', '/')
}

if ($AppId -ne $expectedPlaytestAppId) {
    throw "Refusing to generate SteamPipe scripts for AppID $AppId. Expected Playtest AppID $expectedPlaytestAppId."
}
if ($DepotId -le 0) { throw 'DepotId must be copied from the Steamworks Playtest depot configuration.' }
if ($DepotId -eq $AppId) { throw 'DepotId must not be the same as AppId.' }
if ([string]::IsNullOrWhiteSpace($Description)) { throw 'A non-empty Steam build description is required.' }
if ($Description.Length -gt 100) { throw 'Steam build description must be 100 characters or fewer.' }
if ($Preview -and $SetLiveBranch) { throw 'A preview build cannot be assigned to a live branch.' }

$resolvedContentRoot = Resolve-Path -LiteralPath $ContentRoot
$files = Get-ChildItem -LiteralPath $resolvedContentRoot -Recurse -File
if (!$files) { throw "SteamPipe content root is empty: $resolvedContentRoot" }
foreach ($requiredName in 'LuckyDogRise.exe', 'steam_api64.dll', 'Steamworks.NET.dll', 'build-verification.txt') {
    if (!($files | Where-Object Name -eq $requiredName | Select-Object -First 1)) {
        throw "Required Playtest build file is missing: $requiredName"
    }
}
if ($files | Where-Object Name -eq 'steam_appid.txt' | Select-Object -First 1) {
    throw 'steam_appid.txt must not be uploaded to the Steam depot.'
}
if ($files | Where-Object { $_.Extension -in '.pdb', '.cs', '.psd1' } | Select-Object -First 1) {
    throw 'Debug source, symbols, or local configuration remain in the SteamPipe content root.'
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$resolvedOutput = Resolve-Path -LiteralPath $OutputDirectory
$buildOutput = Join-Path $resolvedOutput 'cache'
New-Item -ItemType Directory -Force -Path $buildOutput | Out-Null

$contentRootVdf = ConvertTo-VdfValue $resolvedContentRoot.Path
$buildOutputVdf = ConvertTo-VdfValue $buildOutput
$descriptionVdf = ConvertTo-VdfValue $Description
$depotFileName = "depot_build_$DepotId.vdf"
$depotPath = Join-Path $resolvedOutput $depotFileName
$appPath = Join-Path $resolvedOutput "app_build_$AppId.vdf"

$depotVdf = @"
"DepotBuild"
{
    "DepotID" "$DepotId"
    "FileMapping"
    {
        "LocalPath" "*"
        "DepotPath" "."
        "Recursive" "1"
    }
    "FileExclusion" "steam_appid.txt"
    "FileExclusion" "*.pdb"
}
"@

$setLiveLine = if ($SetLiveBranch) {
    $branchVdf = ConvertTo-VdfValue $SetLiveBranch
    "    `"SetLive`" `"$branchVdf`"`r`n"
} else { '' }
$previewValue = if ($Preview) { '1' } else { '0' }
$appVdf = @"
"AppBuild"
{
    "AppID" "$AppId"
    "Desc" "$descriptionVdf"
    "Preview" "$previewValue"
$setLiveLine    "ContentRoot" "$contentRootVdf"
    "BuildOutput" "$buildOutputVdf"
    "Depots"
    {
        "$DepotId" "$depotFileName"
    }
}
"@

[System.IO.File]::WriteAllText($depotPath, $depotVdf, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($appPath, $appVdf, [System.Text.UTF8Encoding]::new($false))

Write-Host "[SteamPipe] AppID: $AppId"
Write-Host "[SteamPipe] DepotID: $DepotId"
Write-Host "[SteamPipe] Mode: $(if ($Preview) { 'preview (no upload)' } else { 'upload' })"
Write-Host "[SteamPipe] SetLive: $(if ($SetLiveBranch) { $SetLiveBranch } else { 'none' })"
Write-Host "[SteamPipe] App script: $appPath"
Write-Host "[SteamPipe] Depot script: $depotPath"

[PSCustomObject]@{
    AppScript = $appPath
    DepotScript = $depotPath
    ContentRoot = $resolvedContentRoot.Path
    BuildOutput = $buildOutput
}
