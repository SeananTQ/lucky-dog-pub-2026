@echo off
setlocal EnableExtensions
chcp 65001 >nul

rem This file intentionally contains ASCII only. CMD parses UTF-8 BOM files unreliably.
set "ACHIEVEMENT_ROOT=%~dp0"
for %%I in ("%ACHIEVEMENT_ROOT%..\..") do set "PROJECT_ROOT=%%~fI"

set "BUILD_SCRIPT=%ACHIEVEMENT_ROOT%tools\build-steamworks-achievements.js"
set "INPUT_JSON=%PROJECT_ROOT%\lucky-dog-rise\Data\Json\tbachievement.json"
set "ICON_ROOT=%ACHIEVEMENT_ROOT%icon"
set "GENERATED_ROOT=%ACHIEVEMENT_ROOT%generated"
set "VDF_ROOT=%ACHIEVEMENT_ROOT%vdf"

echo.
echo Steamworks achievement builder
echo JSON: %INPUT_JSON%
echo Icons: %ICON_ROOT%
echo.

node "%BUILD_SCRIPT%" ^
  --input "%INPUT_JSON%" ^
  --icon-root "%ICON_ROOT%" ^
  --generated-root "%GENERATED_ROOT%" ^
  --vdf-root "%VDF_ROOT%"

set "BUILD_EXIT_CODE=%ERRORLEVEL%"
if not "%BUILD_EXIT_CODE%"=="0" (
  echo.
  echo Build failed. See generated\validation-report.json, then try again.
  pause
  exit /b %BUILD_EXIT_CODE%
)

echo.
echo Build succeeded:
echo - %GENERATED_ROOT%\steamworks-achievements.json
echo - %VDF_ROOT%\steamworks-achievements.english.vdf
echo - %VDF_ROOT%\steamworks-achievements.schinese.vdf
pause
