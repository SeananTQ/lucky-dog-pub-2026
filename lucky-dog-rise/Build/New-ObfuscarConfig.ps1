[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$AssemblyDirectory,
    [Parameter(Mandatory)] [string]$OutputDirectory,
    [Parameter(Mandatory)] [string]$ConfigPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$preservePath = Join-Path $PSScriptRoot 'godot-obfuscation-preserve.txt'
$preserved = Get-Content -LiteralPath $preservePath | Where-Object { $_ -and !$_.StartsWith('#') }

$discovered = Get-ChildItem -Recurse -File (Join-Path $projectRoot 'Scripts') -Filter '*.cs' |
    ForEach-Object {
        $sourceText = Get-Content -LiteralPath $_.FullName -Raw
        $namespaceMatch = [regex]::Match($sourceText, '(?m)^namespace\s+([\w.]+)')
        $namespaceName = $namespaceMatch.Groups[1].Value
        foreach ($classMatch in [regex]::Matches($sourceText,
                'public\s+(?:sealed\s+)?partial\s+class\s+(\w+)\s*:\s*(?:Node|Node2D|Control|CanvasLayer|PanelContainer|HBoxContainer|VBoxContainer|Window)\b')) {
            $className = $classMatch.Groups[1].Value
            [pscustomobject]@{
                Name = $className
                FullName = if ($namespaceName) { "$namespaceName.$className" } else { $className }
            }
        }
    } |
    Sort-Object -Property FullName -Unique

$missing = $discovered | Where-Object { $_.Name -notin $preserved -and $_.FullName -notin $preserved }
if ($missing) {
    throw "Godot-bound types are missing from the Obfuscar preserve list: $($missing.FullName -join ', ')"
}

$skipTypes = $preserved | ForEach-Object {
    $entry = $_
    $matchingTypes = @($discovered | Where-Object { $_.Name -eq $entry })
    $fullNames = if ($entry -match '\.') { @($entry) }
        elseif ($matchingTypes.Count) { @($matchingTypes.FullName) }
        else { @("LuckyDogRise.$entry") }
    foreach ($fullName in $fullNames) { "    <SkipType name=`"$fullName`" />" }
}
$skipGodotMethods = $discovered | ForEach-Object {
    "    <SkipMethod type=`"$($_.FullName)`" name=`"*`" />"
}
$assemblyPath = Join-Path (Resolve-Path $AssemblyDirectory) 'LuckyDogRise.dll'
if (!(Test-Path -LiteralPath $assemblyPath)) { throw "Game assembly not found: $assemblyPath" }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$xml = @"
<?xml version="1.0"?>
<Obfuscator>
  <Var name="InPath" value="$([Security.SecurityElement]::Escape((Resolve-Path $AssemblyDirectory).Path))" />
  <Var name="OutPath" value="$([Security.SecurityElement]::Escape((Resolve-Path $OutputDirectory).Path))" />
  <Var name="KeepPublicApi" value="true" />
  <Var name="HidePrivateApi" value="true" />
  <Var name="RenameProperties" value="true" />
  <Var name="RenameEvents" value="true" />
  <Var name="RegenerateDebugInfo" value="false" />
  <Module file="$([Security.SecurityElement]::Escape($assemblyPath))">
$($skipTypes -join "`n")
$($skipGodotMethods -join "`n")
    <SkipMethod type="GodotPlugins.Game.Main" name="InitializeFromGameProject" />
  </Module>
</Obfuscator>
"@
[System.IO.File]::WriteAllText($ConfigPath, $xml, [System.Text.UTF8Encoding]::new($false))
