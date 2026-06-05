@echo off
taskkill /F /IM Wick.Server.exe 2>nul
if %errorlevel%==0 (
    echo Wick processes killed.
) else (
    echo No Wick processes found.
)
