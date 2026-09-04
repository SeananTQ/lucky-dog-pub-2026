[CmdletBinding()]
param(
    [ValidateSet('Generate', 'Preview', 'Upload')] [string]$Action = 'Generate',
    [Parameter(Mandatory)] [ValidateRange(1, [int]::MaxValue)] [int]$DepotId,
    [string]$SteamAccount,
    [string]$Description,
    [string]$SteamCmdPath
)

$ErrorActionPreference = 'Stop'
$demoAppId = 5220880
$placeholderSizeBytes = 28KB
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$workspace = Resolve-Path (Join-Path $projectRoot '..')
$localBuild = Join-Path $workspace '.local-build'
$contentRoot = Join-Path $localBuild 'staging\demo-placeholder'
$scriptOutput = Join-Path $localBuild 'steampipe\demo-placeholder'

if ($DepotId -eq $demoAppId) {
    throw 'The Demo DepotID must not be the same as Demo AppID 5220880.'
}
if ($Action -ne 'Generate' -and [string]::IsNullOrWhiteSpace($SteamAccount)) {
    throw '-SteamAccount is required for Preview or Upload.'
}
if (!$SteamCmdPath) {
    $SteamCmdPath = Join-Path $localBuild 'steamworks\sdk-1.63\tools\ContentBuilder\builder\steamcmd.exe'
}

function ConvertTo-VdfValue([string]$Value) {
    if ($Value.Contains('"')) { throw 'SteamPipe VDF values cannot contain double quotes.' }
    return $Value.Replace('\', '/')
}

if ([string]::IsNullOrWhiteSpace($Description)) {
    $commit = (& git -C $workspace rev-parse --short HEAD).Trim()
    if ([string]::IsNullOrWhiteSpace($commit)) { $commit = 'unknown' }
    $Description = "Lucky Dog Rise Demo placeholder ($commit)"
}
if ($Description.Length -gt 100) {
    throw 'Steam build description must be 100 characters or fewer.'
}

if (Test-Path -LiteralPath $contentRoot) {
    Remove-Item -Recurse -Force -LiteralPath $contentRoot
}
New-Item -ItemType Directory -Force -Path $contentRoot, $scriptOutput | Out-Null

$placeholderPath = Join-Path $contentRoot 'LuckyDogRise.exe'
$header = [System.Text.Encoding]::UTF8.GetBytes(
    "Lucky Dog Rise Demo placeholder`nAppID: $demoAppId`nThis is not a playable review build.`n")
$placeholder = [byte[]]::new($placeholderSizeBytes)
[Array]::Copy($header, $placeholder, [Math]::Min($header.Length, $placeholder.Length))
[System.IO.File]::WriteAllBytes($placeholderPath, $placeholder)

$resolvedContentRoot = (Resolve-Path -LiteralPath $contentRoot).Path
$resolvedScriptOutput = (Resolve-Path -LiteralPath $scriptOutput).Path
$buildOutput = Join-Path $resolvedScriptOutput 'cache'
New-Item -ItemType Directory -Force -Path $buildOutput | Out-Null

$contentRootVdf = ConvertTo-VdfValue $resolvedContentRoot
$buildOutputVdf = ConvertTo-VdfValue $buildOutput
$descriptionVdf = ConvertTo-VdfValue $Description
$depotFileName = "depot_build_$DepotId.vdf"
$depotPath = Join-Path $resolvedScriptOutput $depotFileName
$appPath = Join-Path $resolvedScriptOutput "app_build_$demoAppId.vdf"
$previewValue = if ($Action -eq 'Preview') { '1' } else { '0' }

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
    "FileExclusion" "steam_appid.dev.txt"
}
"@

$appVdf = @"
"AppBuild"
{
    "AppID" "$demoAppId"
    "Desc" "$descriptionVdf"
    "Preview" "$previewValue"
    "ContentRoot" "$contentRootVdf"
    "BuildOutput" "$buildOutputVdf"
    "Depots"
    {
        "$DepotId" "$depotFileName"
    }
}
"@

[System.IO.File]::WriteAllText($depotPath, $depotVdf, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($appPath, $appVdf, [System.Text.UTF8Encoding]::new($false))

Write-Host "[SteamPipe] Demo AppID: $demoAppId"
Write-Host "[SteamPipe] Demo DepotID: $DepotId"
Write-Host "[SteamPipe] Placeholder: $placeholderPath ($placeholderSizeBytes bytes)"
Write-Host "[SteamPipe] App script: $appPath"
Write-Host "[SteamPipe] Depot script: $depotPath"
Write-Host '[SteamPipe] SetLive: none (assign a branch manually in Steamworks if needed)'

if ($Action -eq 'Generate') {
    Write-Host '[SteamPipe] Placeholder and VDF files generated. SteamCMD was not started.'
    return
}
if (!(Test-Path -LiteralPath $SteamCmdPath)) {
    throw "SteamCMD was not found: $SteamCmdPath"
}

Write-Host "[SteamPipe] SteamCMD will log in as '$SteamAccount'."
Write-Host '[SteamPipe] Password and Steam Guard code, if requested, are entered in SteamCMD and are not stored.'
if ($Action -eq 'Upload') {
    Write-Host '[SteamPipe] This uploads a non-playable placeholder Build only; it does not set a live branch or submit review.'
}

& $SteamCmdPath +login $SteamAccount +run_app_build $appPath +quit
if ($LASTEXITCODE -ne 0) {
    throw "SteamCMD failed with exit code $LASTEXITCODE."
}

Write-Host "[SteamPipe] $Action completed successfully."
