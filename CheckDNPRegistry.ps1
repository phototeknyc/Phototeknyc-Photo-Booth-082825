# PowerShell script to check DNP printer registry settings
Write-Host "Checking DNP Printer Registry Settings..." -ForegroundColor Cyan

# Common locations where printer drivers store settings
$paths = @(
    "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Printers\DS40",
    "HKLM:\SOFTWARE\DNP",
    "HKCU:\Software\DNP",
    "HKCU:\Printers\Settings\DS40",
    "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Printers\DS40\PrinterDriverData",
    "HKCU:\Software\Microsoft\Windows NT\CurrentVersion\PrinterPorts",
    "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Print\Printers\DS40"
)

foreach ($path in $paths) {
    if (Test-Path $path) {
        Write-Host "`nFound: $path" -ForegroundColor Green
        try {
            $items = Get-ItemProperty -Path $path -ErrorAction SilentlyContinue
            $items.PSObject.Properties | Where-Object { $_.Name -notlike "PS*" } | ForEach-Object {
                if ($_.Name -like "*cut*" -or $_.Name -like "*2*" -or $_.Name -like "*inch*") {
                    Write-Host "  $($_.Name): $($_.Value)" -ForegroundColor Yellow
                } else {
                    Write-Host "  $($_.Name): $($_.Value)"
                }
            }
        } catch {
            Write-Host "  Could not read properties" -ForegroundColor Red
        }
    } else {
        Write-Host "Not found: $path" -ForegroundColor Gray
    }
}

# Check for DS40 specific keys
Write-Host "`nSearching for all DS40 related registry keys..." -ForegroundColor Cyan
$ds40Keys = Get-ChildItem -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Printers" -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*DS40*" }
foreach ($key in $ds40Keys) {
    Write-Host "Found DS40 key: $($key.Name)" -ForegroundColor Green
    
    # Check PrinterDriverData subkey which often contains driver-specific settings
    $driverDataPath = "$($key.PSPath)\PrinterDriverData"
    if (Test-Path $driverDataPath) {
        Write-Host "  Checking PrinterDriverData..." -ForegroundColor Yellow
        $driverData = Get-ItemProperty -Path $driverDataPath -ErrorAction SilentlyContinue
        $driverData.PSObject.Properties | Where-Object { $_.Name -notlike "PS*" } | ForEach-Object {
            if ($_.Name -like "*cut*" -or $_.Name -like "*2*" -or $_.Name -like "*inch*") {
                Write-Host "    $($_.Name): $($_.Value)" -ForegroundColor Magenta
            }
        }
    }
    
    # Check DsDriver subkey
    $dsDriverPath = "$($key.PSPath)\DsDriver"
    if (Test-Path $dsDriverPath) {
        Write-Host "  Checking DsDriver..." -ForegroundColor Yellow
        $dsDriver = Get-ItemProperty -Path $dsDriverPath -ErrorAction SilentlyContinue
        $dsDriver.PSObject.Properties | Where-Object { $_.Name -notlike "PS*" } | ForEach-Object {
            if ($_.Name -like "*cut*" -or $_.Name -like "*2*" -or $_.Name -like "*inch*") {
                Write-Host "    $($_.Name): $($_.Value)" -ForegroundColor Magenta
            }
        }
    }
}

Write-Host "`nDone checking registry." -ForegroundColor Cyan
Write-Host "Look for any settings related to 'cut', '2inch', or similar." -ForegroundColor Yellow