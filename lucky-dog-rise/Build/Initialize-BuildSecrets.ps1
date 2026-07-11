[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$localBuild = Join-Path $workspace '.local-build'
$secretPath = Join-Path $localBuild 'secrets.psd1'

if (Test-Path -LiteralPath $secretPath) {
    throw "Build secrets already exist at $secretPath. Refusing to overwrite them."
}

New-Item -ItemType Directory -Force -Path $localBuild | Out-Null

function New-HexKey {
    $bytes = [byte[]]::new(32)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToHexString($bytes)
}

$pckKey = New-HexKey
$saveKey = New-HexKey
$content = "@{`n    PckEncryptionKey = '$pckKey'`n    SaveHmacKey = '$saveKey'`n}`n"
[System.IO.File]::WriteAllText($secretPath, $content, [System.Text.UTF8Encoding]::new($false))

Write-Host "Created build secrets at $secretPath"
Write-Host 'Back up this file outside the repository. Its values will not be printed.'
