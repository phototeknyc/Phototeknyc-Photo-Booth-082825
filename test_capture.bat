@echo off
echo ===== Sony FX3 Capture Test =====
echo.
echo Starting photobooth application...
echo Watch for these key events:
echo 1. Sony USB: Sending S1andRelease capture command
echo 2. PhotoCaptured event
echo 3. Any error messages
echo.
echo Press any key after the app loads, then try to capture a photo...
pause
bin\Debug\Photobooth.exe 2>&1 | findstr /I "Sony capture photo S1andRelease PhotoCaptured 0x8402 TransferFile error failed"
echo.
echo Test complete. Check output above.
pause