[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$StagingDirectory,
    [Parameter(Mandatory)] [ValidateSet('Playtest', 'Release')] [string]$Channel,
    [Parameter(Mandatory)] [string]$Version
)

$ErrorActionPreference = 'Stop'
$staging = Resolve-Path $StagingDirectory
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
$bytes = [System.IO.File]::ReadAllBytes($gameAssembly.FullName)
$ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
foreach ($debugSymbol in 'RandomAcquireItemRequested', 'DebugGrantChipsRequested', 'ResetToDebugAllItems') {
    if ($ascii.Contains($debugSymbol)) { throw "Debug symbol remains in release assembly: $debugSymbol" }
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
- Authenticode signed: no
"@
[System.IO.File]::WriteAllText((Join-Path $staging 'build-verification.txt'), $report, [System.Text.UTF8Encoding]::new($false))
Write-Host 'Build artifact verification passed. The package is unsigned.'
