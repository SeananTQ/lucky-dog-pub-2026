@echo off
chcp 65001 >nul
cd /d "%~dp0"
set PYTHONIOENCODING=utf-8
set PYTHONUTF8=1
python scripts\export_wearable_assets.py --config configs\export_wearable_config.json
pause
