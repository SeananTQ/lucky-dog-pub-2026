[CmdletBinding()]
param(
    [string]$GodotSource,
    [switch]$SkipSourceDownload
)

$ErrorActionPreference = 'Stop'
$workspace = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$localBuild = Join-Path $workspace '.local-build'
$secretPath = Join-Path $localBuild 'secrets.psd1'
$configPath = Join-Path $PSScriptRoot 'LocalConfig.psd1'

function Read-LocalDataFile([string]$Path) {
    return & ([scriptblock]::Create([System.IO.File]::ReadAllText($Path)))
}

if (!(Test-Path -LiteralPath $secretPath)) {
    throw 'Build secrets are missing. Run Initialize-BuildSecrets.ps1 first.'
}
if (!$GodotSource -and (Test-Path -LiteralPath $configPath)) {
    $GodotSource = (Read-LocalDataFile $configPath).GodotSource
}
if (!$GodotSource) {
    $GodotSource = Join-Path $localBuild 'godot-4.6.3'
}

foreach ($command in 'git', 'python', 'dotnet') {
    if (!(Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command is unavailable: $command"
    }
}
$sconsOnPath = [bool](Get-Command scons -ErrorAction SilentlyContinue)
if (!$sconsOnPath) {
    & python -m SCons --version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'SCons is unavailable. Install it with: python -m pip install scons' }
}

if (!(Test-Path -LiteralPath (Join-Path $GodotSource 'SConstruct'))) {
    if ($SkipSourceDownload) {
        throw "Godot source is missing at $GodotSource"
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $GodotSource) | Out-Null
    & git clone --branch 4.6.3-stable --depth 1 https://github.com/godotengine/godot.git $GodotSource
    if ($LASTEXITCODE -ne 0) { throw 'Failed to clone Godot 4.6.3 source.' }
}

Push-Location $GodotSource
try {
    & python misc/scripts/install_d3d12_sdk_windows.py
    if ($LASTEXITCODE -ne 0) { throw 'Failed to install Godot D3D12 build dependencies.' }

    $secrets = Read-LocalDataFile $secretPath
    $templateDir = Join-Path $localBuild 'templates'
    $templateOutput = Join-Path $templateDir 'windows_release_x86_64.exe'
    $keyMarker = Join-Path $templateDir 'pck-key.sha256'
    $keyBytes = [System.Text.Encoding]::ASCII.GetBytes($secrets.PckEncryptionKey)
    $keyFingerprint = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($keyBytes))
    $previousKey = $env:SCRIPT_AES256_ENCRYPTION_KEY
    $env:SCRIPT_AES256_ENCRYPTION_KEY = $secrets.PckEncryptionKey
    try {
        $sconsArgs = @('platform=windows', 'target=template_release', 'arch=x86_64', 'module_mono_enabled=yes', 'production=yes', 'lto=none', 'debug_symbols=no')
        $markerMatches = (Test-Path -LiteralPath $keyMarker) -and ((Get-Content -Raw -LiteralPath $keyMarker).Trim() -eq $keyFingerprint)
        if ((Test-Path -LiteralPath $templateOutput) -and !$markerMatches) {
            if ($sconsOnPath) {
                & scons --clean @sconsArgs
            }
            else {
                & python -m SCons --clean @sconsArgs
            }
            if ($LASTEXITCODE -ne 0) { throw 'Failed to clean the stale custom template build.' }
        }
        if ($sconsOnPath) {
            & scons @sconsArgs
        }
        else {
            & python -m SCons @sconsArgs
        }
        if ($LASTEXITCODE -ne 0) { throw 'Godot custom export template compilation failed.' }
    }
    finally {
        $env:SCRIPT_AES256_ENCRYPTION_KEY = $previousKey
    }

    $template = Get-ChildItem -File (Join-Path $GodotSource 'bin') |
        Where-Object { $_.Name -match 'template_release.*x86_64.*\.exe$' -and $_.Name -notmatch 'console' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (!$template) { throw 'Compiled release template was not found in the Godot bin directory.' }

    New-Item -ItemType Directory -Force -Path $templateDir | Out-Null
    Copy-Item -LiteralPath $template.FullName -Destination $templateOutput -Force
    [System.IO.File]::WriteAllText($keyMarker, $keyFingerprint, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Custom template ready: $templateDir"
}
finally {
    Pop-Location
}
