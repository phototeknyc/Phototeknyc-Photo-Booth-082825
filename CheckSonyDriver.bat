@echo off
echo Checking Sony Camera Driver Status...
echo.
echo Looking for Sony USB devices (VID=054C)...
echo.

powershell -Command "Get-PnpDevice | Where-Object { $_.InstanceId -match 'VID_054C' } | Format-Table -Property FriendlyName, Status, Class -AutoSize"

echo.
echo If you see ILME-FX3 listed above, your camera is connected.
echo.
echo To check if you have the correct driver:
echo 1. Open Device Manager
echo 2. Find your Sony camera device
echo 3. Right-click and select Properties
echo 4. Go to Driver tab
echo 5. Check if Driver Provider is "libusbK"
echo.
echo If it shows "Microsoft" or "Sony" as the provider, you need to:
echo 1. Download Zadig from https://zadig.akeo.ie/
echo 2. Run Zadig as Administrator
echo 3. Select your FX3 from the device list
echo 4. Choose "libusbK (v3.1.0.0)" as the driver
echo 5. Click "Replace Driver"
echo.
echo Checking for Imaging Devices...
echo.

powershell -Command "Get-PnpDevice -Class 'Image' | Where-Object { $_.Status -eq 'OK' } | Format-Table -Property FriendlyName -AutoSize"

echo.
pause