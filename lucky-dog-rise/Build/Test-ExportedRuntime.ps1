[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ExecutablePath,
    [int]$RunSeconds = 5
)

$ErrorActionPreference = 'Stop'
$executable = Resolve-Path -LiteralPath $ExecutablePath
$workspace = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$logDirectory = Join-Path $workspace '.local-build\runtime-smoke'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$stdoutPath = Join-Path $logDirectory 'stdout.log'
$stderrPath = Join-Path $logDirectory 'stderr.log'
Remove-Item -Force -ErrorAction SilentlyContinue -LiteralPath $stdoutPath, $stderrPath

$process = Start-Process -FilePath $executable.Path -ArgumentList '--headless' -WindowStyle Hidden -PassThru `
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
    'Unhandled exception',
    'SCRIPT ERROR',
    'Parameter .* is null'
)
$matchedPattern = $fatalPatterns | Where-Object { $output -match $_ } | Select-Object -First 1
if ($matchedPattern) {
    throw "Exported runtime smoke test failed (matched: $matchedPattern). See $logDirectory"
}

Write-Host "[Build] Exported runtime smoke test passed. Logs: $logDirectory"
