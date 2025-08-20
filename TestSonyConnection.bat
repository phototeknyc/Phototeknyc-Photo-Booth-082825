@echo off
echo Testing Sony Camera Connection...
echo ==================================
echo.

cd /d "C:\Users\rakes\OneDrive\Desktop\phototeknycbooth\bin\Debug"

echo Checking for Sony SDK DLLs...
if exist "Cr_Core.dll" (
    echo [OK] Cr_Core.dll found
) else (
    echo [ERROR] Cr_Core.dll NOT found!
)

if exist "CrAdapter\Cr_PTP_USB.dll" (
    echo [OK] Cr_PTP_USB.dll found
) else (
    if exist "Cr_PTP_USB.dll" (
        echo [OK] Cr_PTP_USB.dll found in main directory
    ) else (
        echo [ERROR] Cr_PTP_USB.dll NOT found!
    )
)

if exist "libusb-1.0.dll" (
    echo [OK] libusb-1.0.dll found
) else (
    echo [ERROR] libusb-1.0.dll NOT found!
)

echo.
echo Checking USB devices...
powershell -Command "Get-PnpDevice | Where-Object {$_.HardwareID -like '*VID_054C*' -and $_.Class -eq 'libusbk devices'} | Select-Object Name, Status, Class | Format-Table"

echo.
echo Starting Photobooth with verbose logging...
echo.

REM Create a simple PowerShell script to capture more detailed output
powershell -Command "& { $proc = Start-Process -FilePath '.\Photobooth.exe' -PassThru -WindowStyle Normal; Start-Sleep -Seconds 5; if($proc.HasExited) { Write-Host 'Application exited with code:' $proc.ExitCode } else { Write-Host 'Application started successfully' } }"

pause