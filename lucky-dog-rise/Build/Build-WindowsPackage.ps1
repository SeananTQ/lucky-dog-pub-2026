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
$steamworksNetRoot = Join-Path $localBuild 'steamworks\Steamworks.NET-Standalone_2025.163.0\Windows-x64'
$steamworksManaged = Join-Path $steamworksNetRoot 'Steamworks.NET.dll'
$steamworksNative = Join-Path $steamworksNetRoot 'steam_api64.dll'
$exportPresetPath = Join-Path $projectRoot 'export_presets.cfg'
$presetBackupRoot = Join-Path $localBuild 'preset-backup'
$presetBackupPath = Join-Path $presetBackupRoot 'export_presets.cfg'
$presetAbsentMarker = Join-Path $presetBackupRoot 'export_presets.was-absent'

function Read-LocalDataFile([string]$Path) {
    return & ([scriptblock]::Create([System.IO.File]::ReadAllText($Path)))
}

if (!(Test-Path -LiteralPath $secretPath)) { throw 'Run Initialize-BuildSecrets.ps1 first.' }
if (!(Test-Path -LiteralPath $templatePath)) { throw 'Run Build-CustomTemplate.ps1 first.' }
if (!(Test-Path -LiteralPath $steamworksManaged)) { throw "Steamworks.NET.dll is missing: $steamworksManaged" }
if (!(Test-Path -LiteralPath $steamworksNative)) { throw "steam_api64.dll is missing: $steamworksNative" }
if (!$GodotEditor -and (Test-Path -LiteralPath $configPath)) { $GodotEditor = (Read-LocalDataFile $configPath).GodotEditor }
if (!$GodotEditor -or !(Test-Path -LiteralPath $GodotEditor)) { throw 'Godot 4.6.3 Mono editor path is missing.' }
if ((Test-Path -LiteralPath $presetBackupPath) -or (Test-Path -LiteralPath $presetAbsentMarker)) {
    throw "A stale export preset backup exists at '$presetBackupRoot'. Restore or remove it before starting another package build."
}

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
$outputExe = Join-Path $staging 'LuckyDogRise.exe'
$assemblyPath = Join-Path $staging 'data_LuckyDogRise_windows_x86_64\LuckyDogRise.dll'
$packageDir = Join-Path $workspace 'GameBuild'
$packagePath = Join-Path $packageDir "LuckyDogRise-$version-$channelSlug-win-x64.zip"
if (Test-Path -LiteralPath $staging) { Remove-Item -Recurse -Force -LiteralPath $staging }
New-Item -ItemType Directory -Force -Path $staging, $packageDir | Out-Null

$secrets = Read-LocalDataFile $secretPath
$oldPckKey = $env:GODOT_SCRIPT_ENCRYPTION_KEY
$oldSaveKey = $env:LUCKYDOG_SAVE_HMAC_KEY
$oldCommit = $env:LUCKYDOG_BUILD_COMMIT
$oldPlaytestExpiry = $env:LUCKYDOG_PLAYTEST_EXPIRES_UTC
$hadExportPreset = Test-Path -LiteralPath $exportPresetPath
New-Item -ItemType Directory -Force -Path $presetBackupRoot | Out-Null
if ($hadExportPreset) {
    Copy-Item -LiteralPath $exportPresetPath -Destination $presetBackupPath
}
else {
    [System.IO.File]::WriteAllText($presetAbsentMarker, 'The project had no export_presets.cfg before packaging.')
}

try {
    & (Join-Path $PSScriptRoot 'New-ExportPresets.ps1') -Channel $Channel -TemplatePath $templatePath -ExportPath $outputExe -Version $version
    Write-Host '[Build] Temporary export preset generated.'
    $env:GODOT_SCRIPT_ENCRYPTION_KEY = $secrets.PckEncryptionKey
    $env:LUCKYDOG_SAVE_HMAC_KEY = $secrets.SaveHmacKey
    $env:LUCKYDOG_BUILD_COMMIT = $commit
    $env:LUCKYDOG_PLAYTEST_EXPIRES_UTC = if ($Channel -eq 'Playtest') { '2026-08-11T16:00:00Z' } else { '' }
    try {
        & $GodotEditor --headless --path $projectRoot --export-release "Windows $Channel" $outputExe
        Write-Host "[Build] Godot export exit code: $LASTEXITCODE"
        if ($LASTEXITCODE -ne 0) { throw 'Godot release export failed.' }

        # When the editor already owns the project, the command-line process can
        # return before the editor-side export finishes. Keep the temporary
        # preset in place until both essential output files exist and stop changing.
        for ($attempt = 0; $attempt -lt 150 -and
                (!(Test-Path -LiteralPath $outputExe) -or !(Test-Path -LiteralPath $assemblyPath)); $attempt++) {
            Start-Sleep -Milliseconds 200
        }
        if (!(Test-Path -LiteralPath $outputExe)) { throw "Exported executable was not found: $outputExe" }
        if (!(Test-Path -LiteralPath $assemblyPath)) { throw "Exported game assembly was not found: $assemblyPath" }

        $lastExportSignature = ''
        $stableExportChecks = 0
        for ($attempt = 0; $attempt -lt 50 -and $stableExportChecks -lt 5; $attempt++) {
            $exe = Get-Item -LiteralPath $outputExe
            $dll = Get-Item -LiteralPath $assemblyPath
            $signature = "$($exe.Length):$($exe.LastWriteTimeUtc.Ticks)|$($dll.Length):$($dll.LastWriteTimeUtc.Ticks)"
            if ($signature -eq $lastExportSignature) { $stableExportChecks++ } else { $stableExportChecks = 0 }
            $lastExportSignature = $signature
            Start-Sleep -Milliseconds 200
        }
        if ($stableExportChecks -lt 5) { throw 'Exported files did not become stable before the timeout.' }
    }
    finally {
        $env:GODOT_SCRIPT_ENCRYPTION_KEY = $oldPckKey
        $env:LUCKYDOG_SAVE_HMAC_KEY = $oldSaveKey
        $env:LUCKYDOG_BUILD_COMMIT = $oldCommit
        $env:LUCKYDOG_PLAYTEST_EXPIRES_UTC = $oldPlaytestExpiry
    }
}
finally {
    if ($hadExportPreset) {
        Copy-Item -LiteralPath $presetBackupPath -Destination $exportPresetPath -Force
        Remove-Item -LiteralPath $presetBackupPath -Force
    }
    else {
        if (Test-Path -LiteralPath $exportPresetPath) { Remove-Item -LiteralPath $exportPresetPath -Force }
        Remove-Item -LiteralPath $presetAbsentMarker -Force
    }
    if (!(Get-ChildItem -LiteralPath $presetBackupRoot -Force)) {
        Remove-Item -LiteralPath $presetBackupRoot -Force
    }
    Write-Host '[Build] Original export preset restored.'
}

$assembly = Get-Item -LiteralPath $assemblyPath
Write-Host "[Build] Game assembly found: $assemblyPath"
Copy-Item -LiteralPath $steamworksManaged -Destination $assembly.DirectoryName -Force
Copy-Item -LiteralPath $steamworksNative -Destination $staging -Force
Write-Host '[Build] Steamworks.NET runtime installed (steam_appid.txt intentionally omitted).'
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
