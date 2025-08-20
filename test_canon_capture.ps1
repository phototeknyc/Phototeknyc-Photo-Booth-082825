# Canon Camera Test Script
Write-Host "===== Canon Camera Test =====" -ForegroundColor Cyan
Write-Host ""
Write-Host "This script will test Canon camera functionality" -ForegroundColor Yellow
Write-Host ""
Write-Host "Make sure your Canon camera is:" -ForegroundColor Green
Write-Host "1. Connected via USB"
Write-Host "2. Turned ON"  
Write-Host "3. Set to M (Manual) or P mode"
Write-Host "4. NOT in video mode"
Write-Host ""
Write-Host "The app will launch. Test the following:" -ForegroundColor Yellow
Write-Host "1. Camera should be detected"
Write-Host "2. Live view should work"
Write-Host "3. Photo capture should complete without errors"
Write-Host "4. Files should be saved to the Photos folder"
Write-Host ""
Write-Host "Starting application..." -ForegroundColor Cyan

# Run the app
& ".\bin\Debug\Photobooth.exe"

Write-Host ""
Write-Host "Test completed." -ForegroundColor Cyan