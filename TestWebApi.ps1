# PowerShell script to test Web API connectivity and setup

Write-Host "=== Photobooth Web API Test Script ===" -ForegroundColor Cyan
Write-Host ""

# Test 1: Check if port 8080 is already in use
Write-Host "1. Checking if port 8080 is in use..." -ForegroundColor Yellow
$portInUse = Get-NetTCPConnection -LocalPort 8080 -ErrorAction SilentlyContinue
if ($portInUse) {
    Write-Host "   WARNING: Port 8080 is already in use by:" -ForegroundColor Red
    $portInUse | Format-Table State, OwningProcess
    $process = Get-Process -Id $portInUse[0].OwningProcess -ErrorAction SilentlyContinue
    if ($process) {
        Write-Host "   Process: $($process.Name) (PID: $($process.Id))" -ForegroundColor Red
    }
} else {
    Write-Host "   ✓ Port 8080 is available" -ForegroundColor Green
}
Write-Host ""

# Test 2: Check URL reservation
Write-Host "2. Checking URL reservations..." -ForegroundColor Yellow
$urlAcl = netsh http show urlacl | Select-String "8080"
if ($urlAcl) {
    Write-Host "   Found URL reservation for port 8080:" -ForegroundColor Cyan
    $urlAcl | ForEach-Object { Write-Host "   $_" }
} else {
    Write-Host "   No URL reservation found for port 8080" -ForegroundColor Yellow
    Write-Host "   You may need to add one (requires admin)" -ForegroundColor Yellow
}
Write-Host ""

# Test 3: Try to connect to the API
Write-Host "3. Testing API connection..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:8080/health" -TimeoutSec 2 -ErrorAction Stop
    Write-Host "   ✓ API is responding!" -ForegroundColor Green
    Write-Host "   Response: $($response.Content)" -ForegroundColor Cyan
} catch {
    Write-Host "   ✗ Cannot connect to API at http://localhost:8080" -ForegroundColor Red
    Write-Host "   Error: $_" -ForegroundColor Red
}
Write-Host ""

# Test 4: Check Windows Firewall
Write-Host "4. Checking Windows Firewall..." -ForegroundColor Yellow
$firewallRule = Get-NetFirewallRule -DisplayName "*8080*" -ErrorAction SilentlyContinue
if ($firewallRule) {
    Write-Host "   Found firewall rules for port 8080:" -ForegroundColor Cyan
    $firewallRule | Format-Table DisplayName, Enabled, Action
} else {
    Write-Host "   No specific firewall rules for port 8080" -ForegroundColor Yellow
}
Write-Host ""

# Provide setup instructions
Write-Host "=== Setup Instructions ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "If the API is not accessible, try these steps:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Run as Administrator and add URL reservation:" -ForegroundColor White
Write-Host '   netsh http add urlacl url=http://+:8080/ user=Everyone' -ForegroundColor Gray
Write-Host ""
Write-Host "2. Add Windows Firewall rule (if needed):" -ForegroundColor White
Write-Host '   New-NetFirewallRule -DisplayName "Photobooth API" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow' -ForegroundColor Gray
Write-Host ""
Write-Host "3. If port is in use, try a different port:" -ForegroundColor White
Write-Host "   Edit App.xaml.cs and change 'int webApiPort = 8080' to another port like 8888" -ForegroundColor Gray
Write-Host ""
Write-Host "4. Make sure the Photobooth application is running" -ForegroundColor White
Write-Host ""

# Test alternative ports
Write-Host "=== Alternative Port Suggestions ===" -ForegroundColor Cyan
$altPorts = @(8081, 8082, 8888, 9000, 9090)
Write-Host "Checking alternative ports..." -ForegroundColor Yellow
foreach ($port in $altPorts) {
    $inUse = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue
    if (-not $inUse) {
        Write-Host "   ✓ Port $port is available" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")