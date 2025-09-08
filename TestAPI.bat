@echo off
title Test Photobooth Web API
echo ========================================
echo    Testing Photobooth Web API on Port 8080
echo ========================================
echo.

echo Testing health endpoint...
curl -s http://localhost:8080/health
echo.
echo.

echo Testing camera status...
curl -s http://localhost:8080/api/camera/status
echo.
echo.

echo Testing settings...
curl -s http://localhost:8080/api/settings/all
echo.
echo.

echo ========================================
echo.
echo If you see JSON responses above, the API is working!
echo If not:
echo   1. Make sure Photobooth.exe is running
echo   2. Check that you ran SetupWebApi.bat as administrator
echo   3. Check Windows Firewall settings
echo.
echo You can also open WebApiClient.html in your browser
echo.
pause