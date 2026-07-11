[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Playtest', 'Release')] [string]$Channel,
    [string]$GodotEditor
)

$ErrorActionPreference = 'Stop'
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$workspace = Resolve-Path (Join-Path $projectRoot '..')
$localBuild = Join-Path $workspace '.local-build'
$secretPath = Join-Path $localBuild 'secrets.psd1'
$configPath = Join-Path $PSScriptRoot 'LocalConfig.psd1'
$templatePath = Join-Path $localBuild 'templates\windows_release_x86_64.exe'

function Read-LocalDataFile([string]$Path) {
    return & ([scriptblock]::Create([System.IO.File]::ReadAllText($Path)))
}

if (!(Test-Path -LiteralPath $secretPath)) { throw 'Run Initialize-BuildSecrets.ps1 first.' }
if (!(Test-Path -LiteralPath $templatePath)) { throw 'Run Build-CustomTemplate.ps1 first.' }
if (!$GodotEditor -and (Test-Path -LiteralPath $configPath)) { $GodotEditor = (Read-LocalDataFile $configPath).GodotEditor }
if (!$GodotEditor -or !(Test-Path -LiteralPath $GodotEditor)) { throw 'Godot 4.6.3 Mono editor path is missing.' }

& dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'Failed to restore repository-local .NET tools.' }

$versionLine = Select-String -LiteralPath (Join-Path $projectRoot 'project.godot') -Pattern '^config/version="([^"]+)"$'
if (!$versionLine) { throw 'Project version was not found.' }
$version = $versionLine.Matches[0].Groups[1].Value
$commit = (& git -C $workspace rev-parse --short HEAD).Trim()
$dirty = [bool](& git -C $workspace status --porcelain)
if ($Channel -eq 'Release' -and $dirty) { throw 'Release builds require a clean worktree.' }
if ($dirty) { $commit = "$commit-dirty" }

$channelSlug = $Channel.ToLowerInvariant()
$staging = Join-Path $localBuild "staging\$channelSlug"
$outputExe = Join-Path $staging 'LuckyDogPub.exe'
$packageDir = Join-Path $workspace 'GameBuild'
$packagePath = Join-Path $packageDir "LuckyDogPub-$version-$channelSlug-win-x64.zip"
if (Test-Path -LiteralPath $staging) { Remove-Item -Recurse -Force -LiteralPath $staging }
New-Item -ItemType Directory -Force -Path $staging, $packageDir | Out-Null

& (Join-Path $PSScriptRoot 'New-ExportPresets.ps1') -Channel $Channel -TemplatePath $templatePath -ExportPath $outputExe
Write-Host '[Build] Export preset generated.'
$secrets = Read-LocalDataFile $secretPath
$oldPckKey = $env:GODOT_SCRIPT_ENCRYPTION_KEY
$oldSaveKey = $env:LUCKYDOG_SAVE_HMAC_KEY
$oldCommit = $env:LUCKYDOG_BUILD_COMMIT
$env:GODOT_SCRIPT_ENCRYPTION_KEY = $secrets.PckEncryptionKey
$env:LUCKYDOG_SAVE_HMAC_KEY = $secrets.SaveHmacKey
$env:LUCKYDOG_BUILD_COMMIT = $commit
try {
    & $GodotEditor --headless --path $projectRoot --export-release "Windows $Channel" $outputExe
    Write-Host "[Build] Godot export exit code: $LASTEXITCODE"
    if ($LASTEXITCODE -ne 0) { throw 'Godot release export failed.' }
}
finally {
    $env:GODOT_SCRIPT_ENCRYPTION_KEY = $oldPckKey
    $env:LUCKYDOG_SAVE_HMAC_KEY = $oldSaveKey
    $env:LUCKYDOG_BUILD_COMMIT = $oldCommit
}

$assemblyPath = Join-Path $staging 'data_LuckyDogRise_windows_x86_64\LuckyDogRise.dll'
for ($attempt = 0; $attempt -lt 50 -and !(Test-Path -LiteralPath $assemblyPath); $attempt++) {
    Start-Sleep -Milliseconds 200
}
if (!(Test-Path -LiteralPath $assemblyPath)) { throw "Exported game assembly was not found: $assemblyPath" }
$assembly = Get-Item -LiteralPath $assemblyPath
Write-Host "[Build] Game assembly found: $assemblyPath"
$obfuscationRoot = Join-Path $localBuild "obfuscation\$channelSlug"
$obfuscated = Join-Path $obfuscationRoot 'output'
$obfuscarConfig = Join-Path $obfuscationRoot 'obfuscar.xml'
New-Item -ItemType Directory -Force -Path $obfuscationRoot | Out-Null
& (Join-Path $PSScriptRoot 'New-ObfuscarConfig.ps1') -AssemblyDirectory $assembly.DirectoryName -OutputDirectory $obfuscated -ConfigPath $obfuscarConfig
Write-Host '[Build] Obfuscar configuration generated.'
& dotnet tool run obfuscar.console -- $obfuscarConfig
Write-Host "[Build] Obfuscar exit code: $LASTEXITCODE"
if ($LASTEXITCODE -ne 0) { throw 'Obfuscar failed.' }
$obfuscatedAssembly = Join-Path $obfuscated 'LuckyDogRise.dll'
if (!(Test-Path -LiteralPath $obfuscatedAssembly)) { throw 'Obfuscated assembly was not produced.' }
Copy-Item -LiteralPath $obfuscatedAssembly -Destination $assembly.FullName -Force
Write-Host '[Build] Obfuscated game assembly installed.'

$mapDir = Join-Path $localBuild "maps\$version\$channelSlug"
New-Item -ItemType Directory -Force -Path $mapDir | Out-Null
Get-ChildItem -LiteralPath $obfuscationRoot -Recurse -File | Where-Object { $_.Name -match 'Mapping|map' } |
    Copy-Item -Destination $mapDir -Force
Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object { $_.Extension -eq '.pdb' -or $_.Name -match 'console\.exe$' } |
    Remove-Item -Force

& (Join-Path $PSScriptRoot 'Verify-Build.ps1') -StagingDirectory $staging -Channel $Channel -Version $version
& (Join-Path $PSScriptRoot 'Test-ExportedRuntime.ps1') -ExecutablePath $outputExe
if (Test-Path -LiteralPath $packagePath) { Remove-Item -Force -LiteralPath $packagePath }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $packagePath -CompressionLevel Optimal
Write-Host "Package ready: $packagePath"
