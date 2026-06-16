@echo off
chcp 65001 >nul
cd /d "%~dp0"
python export_all_layers.py --config export_all_config.json
pause
