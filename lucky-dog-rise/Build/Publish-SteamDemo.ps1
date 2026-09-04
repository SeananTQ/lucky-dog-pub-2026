[CmdletBinding()]
param(
    [ValidateSet('Generate', 'Preview', 'Upload')] [string]$Action = 'Generate',
    [string]$Description,
    [string]$SetLiveBranch,
    [switch]$SkipPackageBuild,
    [string]$GodotEditor
)

$ErrorActionPreference = 'Stop'
$expectedAppId = 5220880
$expectedDepotId = 5220881
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$workspace = Resolve-Path (Join-Path $projectRoot '..')
$localBuild = Join-Path $workspace '.local-build'
$configPath = Join-Path $PSScriptRoot 'SteamDemoPipeConfig.psd1'
$exampleConfigPath = Join-Path $PSScriptRoot 'SteamDemoPipeConfig.example.psd1'
$steamCmd = Join-Path $localBuild 'steamworks\sdk-1.63\tools\ContentBuilder\builder\steamcmd.exe'
$staging = Join-Path $localBuild 'staging\demo'
$scriptOutput = Join-Path $localBuild 'steampipe\demo'

function Read-LocalDataFile([string]$Path) {
    return & ([scriptblock]::Create([System.IO.File]::ReadAllText($Path)))
}

$config = $null
if (Test-Path -LiteralPath $configPath) {
    $config = Read-LocalDataFile $configPath
    if ($config.AppId -ne $expectedAppId) { throw "SteamDemoPipeConfig.psd1 must use Demo AppID $expectedAppId." }
    if ($config.DepotId -ne $expectedDepotId) { throw "SteamDemoPipeConfig.psd1 must use Demo DepotID $expectedDepotId." }
}
elseif ($Action -ne 'Generate') {
    throw "Steam Demo config is missing. Copy '$exampleConfigPath' to '$configPath', then fill SteamAccount."
}
if ($Action -ne 'Generate' -and [string]::IsNullOrWhiteSpace($config.SteamAccount)) {
    throw 'SteamDemoPipeConfig.psd1 SteamAccount is required for Preview or Upload.'
}
if ($Action -ne 'Upload' -and $SetLiveBranch) {
    throw '-SetLiveBranch is accepted only with -Action Upload.'
}

if (!$SkipPackageBuild) {
    & (Join-Path $PSScriptRoot 'Build-WindowsPackage.ps1') -Channel Demo -GodotEditor $GodotEditor
    if ($LASTEXITCODE -ne 0) { throw 'Demo package build failed.' }
}
if (!(Test-Path -LiteralPath $staging)) {
    throw "Demo staging directory is missing: $staging. Build it first or omit -SkipPackageBuild."
}

$versionLine = Select-String -LiteralPath (Join-Path $projectRoot 'project.godot') -Pattern '^config/version="([^"]+)"$'
if (!$versionLine) { throw 'Project version was not found.' }
$version = $versionLine.Matches[0].Groups[1].Value
$commit = (& git -C $workspace rev-parse --short HEAD).Trim()
$dirty = [bool](& git -C $workspace status --porcelain)
if ($dirty) { $commit = "$commit-dirty" }
if ([string]::IsNullOrWhiteSpace($Description)) {
    $Description = "Lucky Dog Rise Demo $version ($commit)"
}

& (Join-Path $PSScriptRoot 'Verify-Build.ps1') -StagingDirectory $staging -Channel Demo -Version $version
$generated = & (Join-Path $PSScriptRoot 'New-SteamPipeScripts.ps1') `
    -Channel Demo `
    -ContentRoot $staging `
    -OutputDirectory $scriptOutput `
    -AppId $expectedAppId `
    -DepotId $expectedDepotId `
    -Description $Description `
    -Preview:($Action -eq 'Preview') `
    -SetLiveBranch $SetLiveBranch

if ($Action -eq 'Generate') {
    Write-Host '[SteamPipe] Demo configuration generated and validated. SteamCMD was not started.'
    return
}
if (!(Test-Path -LiteralPath $steamCmd)) { throw "SteamCMD from SDK 1.63 is missing: $steamCmd" }

Write-Host "[SteamPipe] SteamCMD will log in as '$($config.SteamAccount)'."
Write-Host '[SteamPipe] Password and Steam Guard code, if requested, must be entered in SteamCMD and are not stored by this script.'
if ($Action -eq 'Upload' -and !$SetLiveBranch) {
    Write-Host '[SteamPipe] The build will be uploaded but not assigned to a live branch.'
}

& $steamCmd +login $config.SteamAccount +run_app_build $generated.AppScript +quit
if ($LASTEXITCODE -ne 0) { throw "SteamCMD failed with exit code $LASTEXITCODE." }

Write-Host "[SteamPipe] Demo $Action completed successfully."
