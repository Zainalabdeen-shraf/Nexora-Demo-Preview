@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET SDK 10 is required. Install it, then run this file again.
    pause
    exit /b 1
)
dotnet run --project "%~dp0Nexora.Demo.Desktop\Nexora.Demo.Desktop.csproj"
if errorlevel 1 pause
endlocal
