@echo off
cd /d "%~dp0\..\publish"
FinzatiDailyReport.exe --no-send
pause
