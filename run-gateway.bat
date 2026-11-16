@echo off
setlocal

title Rostek Industrial Gateway
set "HOST_DIR=%~dp0src\Rostek.Gateway.Host"
cd /d "%HOST_DIR%"

set "DOTNET_EXE=%USERPROFILE%\.dotnet\dotnet.exe"
if not exist "%DOTNET_EXE%" (
    where dotnet >nul 2>&1
    if errorlevel 1 (
        echo [ERROR] .NET SDK was not found.
        echo Install .NET 10 SDK or add dotnet.exe to PATH.
        echo Expected local path: %USERPROFILE%\.dotnet\dotnet.exe
        pause
        exit /b 1
    )

    set "DOTNET_EXE=dotnet"
)

echo Starting Rostek Industrial Gateway...
echo Project: %HOST_DIR%\Rostek.Gateway.Host.csproj
echo Config : %USERPROFILE%\.gateway\appsettings.json
echo Press Ctrl+C to stop the application.
echo.

"%DOTNET_EXE%" run ^
    --project "Rostek.Gateway.Host.csproj" ^
    --configuration Release ^
    --no-launch-profile

set "EXIT_CODE=%ERRORLEVEL%"
echo.
if not "%EXIT_CODE%"=="0" (
    echo [ERROR] Gateway stopped with exit code %EXIT_CODE%.
) else (
    echo Gateway stopped.
)

pause
exit /b %EXIT_CODE%
