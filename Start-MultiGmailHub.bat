@echo off
cd /d "%~dp0"
echo Starting Multi-Gmail Client Hub (100-150+ Accounts Manager)...
start "" "http://localhost:8765"
MultiGmailManager.exe
pause
