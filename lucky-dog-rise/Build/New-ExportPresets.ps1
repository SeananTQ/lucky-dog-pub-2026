[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Playtest', 'Release')] [string]$Channel,
    [Parameter(Mandatory)] [string]$TemplatePath,
    [Parameter(Mandatory)] [string]$ExportPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$feature = if ($Channel -eq 'Playtest') { 'lucky_playtest' } else { 'lucky_release' }
$presetName = "Windows $Channel"
$escapedTemplate = $TemplatePath.Replace('\', '/')
$escapedExport = $ExportPath.Replace('\', '/')
$content = @"
[preset.0]

name="$presetName"
platform="Windows Desktop"
runnable=true
dedicated_server=false
custom_features="$feature"
export_filter="all_resources"
include_filter="Data/Localization/*.csv,Audio/**/*.ogg,Audio/**/*.wav,Audio/**/*.mp3"
exclude_filter="addons/*,Tools/*,docs/*,Scenes/Test*.tscn,Scenes/UIThemePreview.tscn,Scenes/LocalizationLayoutLab.tscn,Assets/v1/layer_index_old.json,Themes/DefaultTheme_V1Bak.tres"
export_path="$escapedExport"
patches=PackedStringArray()
encryption_include_filters="*"
encryption_exclude_filters=""
seed=0
encrypt_pck=true
encrypt_directory=true
script_export_mode=2

[preset.0.options]

custom_template/debug=""
custom_template/release="$escapedTemplate"
debug/export_console_wrapper=0
binary_format/embed_pck=true
texture_format/s3tc_bptc=true
texture_format/etc2_astc=false
shader_baker/enabled=false
binary_format/architecture="x86_64"
codesign/enable=false
application/modify_resources=true
application/file_version=""
application/product_version=""
application/product_name="Lucky Dog Pub"
application/file_description="Lucky Dog Pub"
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=false
dotnet/embed_build_outputs=false
"@

[System.IO.File]::WriteAllText((Join-Path $projectRoot 'export_presets.cfg'), $content, [System.Text.UTF8Encoding]::new($false))
