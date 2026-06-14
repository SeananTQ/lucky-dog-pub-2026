@echo off
cd /d "%~dp0"
python csvdiff.py %*
pause