@echo off
cd /d "%~dp0"
python ___csvdiff.py %*
pause