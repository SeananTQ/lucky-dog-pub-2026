@echo off
chcp 65001 >nul
cd /d "%~dp0"
python scripts\export_all_layers.py --config configs\export_all_config.json
pause
