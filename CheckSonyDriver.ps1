# PowerShell script to check Sony camera driver status

Write-Host "Checking Sony Camera USB Driver Status..." -ForegroundColor Cyan
Write-Host ""

# Check for Sony camera devices
Write-Host "Looking for Sony USB devices (VID=054C)..." -ForegroundColor Yellow
$sonyDevices = Get-PnpDevice | Where-Object { $_.InstanceId -match "VID_054C" }

if ($sonyDevices) {
    Write-Host "Found Sony devices:" -ForegroundColor Green
    foreach ($device in $sonyDevices) {
        Write-Host "  - $($device.FriendlyName)" -ForegroundColor White
        Write-Host "    Status: $($device.Status)" -ForegroundColor Gray
        Write-Host "    Device ID: $($device.InstanceId)" -ForegroundColor Gray
        Write-Host "    Class: $($device.Class)" -ForegroundColor Gray
        
        # Check driver details
        $driver = Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName DEVPKEY_Device_DriverDesc -ErrorAction SilentlyContinue
        if ($driver) {
            Write-Host "    Driver: $($driver.Data)" -ForegroundColor Gray
        }
    }
    
    Write-Host ""
    
    # Check if it's using WinUSB or libusbK driver
    $driverProvider = Get-PnpDeviceProperty -InstanceId $sonyDevices[0].InstanceId -KeyName DEVPKEY_Device_DriverProvider -ErrorAction SilentlyContinue
    if ($driverProvider) {
        Write-Host "Driver Provider: $($driverProvider.Data)" -ForegroundColor Yellow
        
        if ($driverProvider.Data -match "libusb") {
            Write-Host "✓ libusbK driver detected (required for Sony SDK)" -ForegroundColor Green
        } elseif ($driverProvider.Data -match "Microsoft") {
            Write-Host "⚠ Using Windows default driver" -ForegroundColor Yellow
            Write-Host "  Sony SDK requires libusbK driver for PC Remote mode" -ForegroundColor Yellow
            Write-Host "  Install libusbK driver using Zadig:" -ForegroundColor Yellow
            Write-Host "  1. Download Zadig from https://zadig.akeo.ie/" -ForegroundColor White
            Write-Host "  2. Connect camera in PC Remote mode" -ForegroundColor White
            Write-Host "  3. In Zadig, select the Sony camera device" -ForegroundColor White
            Write-Host "  4. Choose libusbK as the driver" -ForegroundColor White
            Write-Host "  5. Click Replace Driver" -ForegroundColor White
        } else {
            Write-Host "  Driver provider: $($driverProvider.Data)" -ForegroundColor Gray
        }
    }
    
} else {
    Write-Host "No Sony USB devices found" -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting steps:" -ForegroundColor Yellow
    Write-Host "1. Make sure camera is connected via USB" -ForegroundColor White
    Write-Host "2. Turn on the camera" -ForegroundColor White
    Write-Host "3. Set camera to PC Remote mode:" -ForegroundColor White
    Write-Host "   - Menu → Setup → USB → USB Connection → PC Remote" -ForegroundColor Gray
    Write-Host "4. Try a different USB cable (USB-C data cable required)" -ForegroundColor White
    Write-Host "5. Try a different USB port" -ForegroundColor White
}

Write-Host ""
Write-Host "Checking for Imaging Devices..." -ForegroundColor Yellow
$imagingDevices = Get-PnpDevice -Class Image | Where-Object { $_.Status -eq "OK" }
if ($imagingDevices) {
    Write-Host "Found imaging devices:" -ForegroundColor Green
    foreach ($device in $imagingDevices) {
        Write-Host "  - $($device.FriendlyName)" -ForegroundColor White
    }
} else {
    Write-Host "No imaging devices found" -ForegroundColor Red
}

Write-Host ""
Write-Host "Press Enter to exit..."
Read-Host