@echo off
cd /d "%~dp0\..\publish"
FinzatiDailyReport.exe
exit /b %errorlevel%
