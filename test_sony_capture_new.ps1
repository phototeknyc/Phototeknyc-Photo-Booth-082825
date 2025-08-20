# Sony FX3 Camera Test Script
Write-Host "===== Sony FX3 Camera Test =====`n" -ForegroundColor Cyan

Write-Host "This script will test Sony FX3 camera functionality" -ForegroundColor Yellow
Write-Host ""
Write-Host "Camera Setup Requirements:" -ForegroundColor Green
Write-Host "1. Connect Sony FX3 via USB cable"
Write-Host "2. Turn ON the camera"
Write-Host "3. Set camera to Still photo mode (not Movie mode)"
Write-Host "4. Navigate to: Menu -> Setup -> USB -> USB Connection Mode"
Write-Host "5. Set to 'PC Remote' mode"
Write-Host "6. Exit menu system back to shooting mode"
Write-Host ""
Write-Host "Testing checklist:" -ForegroundColor Yellow
Write-Host "1. Camera should be detected as 'Sony FX3'"
Write-Host "2. Live view should display camera feed"
Write-Host "3. When you click capture:"
Write-Host "   - Camera shutter should fire (audible click)"
Write-Host "   - Photo should be saved to Photos folder"
Write-Host "4. Check logs for S1andRelease Down/Up commands"
Write-Host ""
Write-Host "Press any key to launch the app..." -ForegroundColor Cyan
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host "`nStarting application..." -ForegroundColor Cyan

# Run the app
& ".\bin\Debug\Photobooth.exe"

Write-Host ""
Write-Host "Test completed." -ForegroundColor Cyan
Write-Host ""
Write-Host "If the camera didn't fire, check:" -ForegroundColor Yellow
Write-Host "- Camera is in PC Remote mode"
Write-Host "- Camera is set to Still mode (not Movie)"
Write-Host "- USB cable is properly connected"
Write-Host "- Latest Sony drivers are installed"