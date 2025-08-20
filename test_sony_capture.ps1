# Sony FX3 Photo Capture Test Script
Write-Host "===== Sony FX3 Photo Capture Test =====" -ForegroundColor Cyan
Write-Host "This script will test the Sony FX3 camera capture with the NullReference fix" -ForegroundColor Yellow
Write-Host ""
Write-Host "Instructions:" -ForegroundColor Green
Write-Host "1. The photobooth app will launch"
Write-Host "2. Go to the photo capture screen"  
Write-Host "3. Click the START button to begin photo sequence"
Write-Host "4. Watch for any NullReferenceException errors"
Write-Host "5. Check if photo is captured successfully"
Write-Host ""
Write-Host "Starting application..." -ForegroundColor Cyan

# Run the app
& ".\bin\Debug\Photobooth.exe"

Write-Host ""
Write-Host "Test completed. Check the logs above for any errors." -ForegroundColor Cyan