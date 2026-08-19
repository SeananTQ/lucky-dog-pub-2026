@echo off
chcp 65001 >nul
cd /d "%~dp0"
set PYTHONIOENCODING=utf-8
set PYTHONUTF8=1
python "%~dp0pyside6_ui_probe.py"
if errorlevel 1 pause
