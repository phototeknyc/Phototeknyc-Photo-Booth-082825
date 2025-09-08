@echo off
echo Testing Photobooth Web API...
echo.

echo Testing port 49152 (new default):
curl -s http://localhost:49152/health
echo.
echo.

echo Testing port 8080 (old default):
curl -s http://localhost:8080/health
echo.
echo.

echo If you see JSON output above, the API is working!
echo If not, check that the Photobooth application is running.
echo.
echo You can also open WebApiClient.html in a browser to test.
echo.
pause