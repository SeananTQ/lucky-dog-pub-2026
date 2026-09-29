[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$GodotPath,
    [switch]$Smoke,
    [switch]$UiSmoke
)

$ErrorActionPreference = 'Stop'
if ($Smoke -and $UiSmoke) { throw 'Choose Smoke or UiSmoke, not both.' }
$projectDirectory = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$workspaceDirectory = Split-Path -Parent $projectDirectory
$runtimeExecutable = (Resolve-Path -LiteralPath $GodotPath).Path
$logDirectory = Join-Path $workspaceDirectory '.local-build/room-lab'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget/packages'
& dotnet build (Join-Path $projectDirectory 'LuckyDogRise.csproj') --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Room lab build failed.' }
$launchArguments = @('--path', $projectDirectory, '--log-file',
    (Join-Path $logDirectory 'launcher.log'), 'res://Scenes/Dev/Rooms/RoomLab.tscn')
if ($Smoke) { $launchArguments = @('--headless') + $launchArguments + @('--', '--rooms-smoke') }
if ($UiSmoke) { $launchArguments += @('--', '--rooms-ui-smoke') }
& $runtimeExecutable @launchArguments
