[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$toolRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverScript = Join-Path $toolRoot 'ui-server.js'
$url = 'http://127.0.0.1:43117/'

if (!(Get-Command node -ErrorAction SilentlyContinue)) {
    throw 'Node.js was not found in PATH.'
}

$alreadyRunning = $false
try {
    $response = Invoke-WebRequest -Uri ($url + 'api/preview') -UseBasicParsing -TimeoutSec 1
    $alreadyRunning = $response.StatusCode -eq 200
} catch {
    $alreadyRunning = $false
}

if (!$alreadyRunning) {
    Start-Process -FilePath 'node' -ArgumentList @($serverScript) -WorkingDirectory $toolRoot -WindowStyle Hidden | Out-Null

    $started = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 100
        try {
            $response = Invoke-WebRequest -Uri ($url + 'api/preview') -UseBasicParsing -TimeoutSec 1
            if ($response.StatusCode -eq 200) {
                $started = $true
                break
            }
        } catch {
        }
    }
    if (!$started) {
        throw 'Steam ItemDef UI did not start. Run node ui-server.js to inspect the error.'
    }
}

Start-Process $url
