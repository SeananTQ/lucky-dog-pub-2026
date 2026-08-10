[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ExecutablePath,
    [int]$RunSeconds = 10
)

$ErrorActionPreference = 'Stop'
$executable = Resolve-Path -LiteralPath $ExecutablePath
$workspace = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$logDirectory = Join-Path $workspace '.local-build\runtime-smoke'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$stdoutPath = Join-Path $logDirectory 'stdout.log'
$stderrPath = Join-Path $logDirectory 'stderr.log'
Remove-Item -Force -ErrorAction SilentlyContinue -LiteralPath $stdoutPath, $stderrPath
$isolatedAppData = Join-Path $logDirectory ("appdata-" + [Guid]::NewGuid().ToString('N'))
$isolatedLocalAppData = Join-Path $logDirectory ("localappdata-" + [Guid]::NewGuid().ToString('N'))
$diagnosticExportDirectory = Join-Path $logDirectory ("diagnostic-exports-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $isolatedAppData, $isolatedLocalAppData, $diagnosticExportDirectory | Out-Null

$process = Start-Process -FilePath $executable.Path -ArgumentList @('--headless', '--', '--diagnostics-export-smoke') -WindowStyle Hidden -PassThru `
    -Environment @{ APPDATA = $isolatedAppData; LOCALAPPDATA = $isolatedLocalAppData; LUCKYDOG_DIAGNOSTICS_SMOKE_DIR = $diagnosticExportDirectory } `
    -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
try {
    if (!$process.WaitForExit($RunSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}
finally {
    if (!$process.HasExited) { Stop-Process -Id $process.Id -Force }
}

$output = @(
    if (Test-Path -LiteralPath $stdoutPath) { Get-Content -Raw -LiteralPath $stdoutPath }
    if (Test-Path -LiteralPath $stderrPath) { Get-Content -Raw -LiteralPath $stderrPath }
) -join "`n"
$fatalPatterns = @(
    "Couldn't load project data",
    'Failed to get GodotPlugins initialization function pointer',
    '\[Audio\] Exported SFX directory is missing',
    '\[Audio\] Required audio resource is missing',
    '\[Audio\] Required SFX cue cannot be resolved',
    'Unhandled exception',
    'SCRIPT ERROR',
    '\[Diagnostics\] Failed',
    '\[DiagnosticsSmoke\] Export failed',
    'Parameter .* is null'
)
$matchedPattern = $fatalPatterns | Where-Object { $output -match $_ } | Select-Object -First 1
if ($matchedPattern) {
    throw "Exported runtime smoke test failed (matched: $matchedPattern). See $logDirectory"
}

if ($output -notmatch '\[DiagnosticsSmoke\] Export passed:') {
    throw "Exported runtime smoke test did not complete the diagnostic export. See $logDirectory"
}

$diagnosticPackages = @(Get-ChildItem -LiteralPath $diagnosticExportDirectory -Filter 'LuckyDogRise-Diagnostics-*.zip')
if ($diagnosticPackages.Count -ne 1) {
    throw "Expected exactly one diagnostic package, found $($diagnosticPackages.Count): $diagnosticExportDirectory"
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($diagnosticPackages[0].FullName)
try {
    $entryNames = @($archive.Entries | ForEach-Object FullName)
    $hasSummary = 'diagnostic-summary.json' -in $entryNames
    $hasEvents = @($entryNames | Where-Object { $_ -like 'events/events-*.jsonl' }).Count -gt 0
    $hasGodotLog = @($entryNames | Where-Object { $_ -like 'logs/godot*.log' }).Count -gt 0
    if (!$hasSummary -or !$hasEvents -or !$hasGodotLog) {
        throw "Diagnostic package is missing required entries: $($entryNames -join ', ')"
    }
}
finally {
    $archive.Dispose()
}

Write-Host "[Build] Exported runtime smoke test passed. Logs: $logDirectory"
