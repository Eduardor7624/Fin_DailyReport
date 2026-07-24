@echo off
setlocal
cd /d "%~dp0\.."
dotnet restore
if errorlevel 1 exit /b 1
dotnet publish -c Release -r win-x64 --self-contained false -o ".\publish"
if errorlevel 1 exit /b 1
copy /Y ".\appsettings.json" ".\publish\appsettings.json" >nul
echo.
echo Published to: %CD%\publish
endlocal
