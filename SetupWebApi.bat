@echo off
title Photobooth Web API Setup
color 0A

echo =====================================
echo   Photobooth Web API Setup Helper
echo =====================================
echo.

:: Check for admin privileges
net session >nul 2>&1
if %errorLevel% == 0 (
    echo [OK] Running with Administrator privileges
) else (
    echo [ERROR] This script requires Administrator privileges!
    echo.
    echo Please right-click this file and select "Run as administrator"
    echo.
    pause
    exit /b 1
)

echo.
echo This script will configure your system to allow the Photobooth Web API
echo to listen on port 8080 (or another port of your choice).
echo.
echo =====================================
echo.

:: Check if port 8080 is in use
echo Checking if port 8080 is available...
netstat -an | findstr :8080 | findstr LISTENING >nul
if %errorLevel% == 0 (
    echo [WARNING] Port 8080 appears to be in use!
    echo.
    echo You can either:
    echo 1. Stop the application using port 8080
    echo 2. Choose a different port
    echo.
    set /p PORT="Enter port number (or press Enter for 8080): "
    if "%PORT%"=="" set PORT=8080
) else (
    echo [OK] Port 8080 is available
    set PORT=8080
    echo.
    set /p CUSTOM="Use default port 8080? (Y/N): "
    if /i "%CUSTOM%"=="N" (
        set /p PORT="Enter port number: "
    )
)

echo.
echo Using port: %PORT%
echo.

:: Add URL reservation
echo Adding URL reservation for port %PORT%...
netsh http add urlacl url=http://+:%PORT%/ user=Everyone >nul 2>&1
if %errorLevel% == 0 (
    echo [OK] URL reservation added successfully
) else (
    echo [INFO] URL reservation might already exist or failed to add
    echo Checking existing reservations...
    netsh http show urlacl | findstr :%PORT%
)

echo.

:: Add firewall rule
echo Adding Windows Firewall rule...
netsh advfirewall firewall add rule name="Photobooth Web API (Port %PORT%)" dir=in action=allow protocol=TCP localport=%PORT% >nul 2>&1
if %errorLevel% == 0 (
    echo [OK] Firewall rule added successfully
) else (
    echo [INFO] Firewall rule might already exist
)

echo.
echo =====================================
echo   Setup Complete!
echo =====================================
echo.
echo The Web API should now be accessible at:
echo   http://localhost:%PORT%/
echo.
echo API Endpoints:
echo   http://localhost:%PORT%/health           - Check API health
echo   http://localhost:%PORT%/api/camera/status - Camera status
echo   http://localhost:%PORT%/api/settings/all  - Get all settings
echo.
echo Next steps:
echo 1. Start the Photobooth application
echo 2. Open WebApiClient.html in a web browser
echo 3. Or test with: curl http://localhost:%PORT%/health
echo.

if NOT "%PORT%"=="8080" (
    echo IMPORTANT: You chose port %PORT% instead of 8080
    echo You need to update App.xaml.cs:
    echo   Change: int webApiPort = 8080;
    echo   To:     int webApiPort = %PORT%;
    echo.
)

pause