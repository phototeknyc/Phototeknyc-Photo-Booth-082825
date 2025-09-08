# Check Cloud Sync Status
Write-Host "Checking Cloud Sync Status..." -ForegroundColor Cyan

# Check AWS credentials
$bucketName = [Environment]::GetEnvironmentVariable("S3_BUCKET_NAME", "User")
$accessKey = [Environment]::GetEnvironmentVariable("AWS_ACCESS_KEY_ID", "User")

if ($bucketName -and $accessKey) {
    Write-Host "AWS Configured: YES" -ForegroundColor Green
    Write-Host "Bucket: $bucketName" -ForegroundColor Gray
} else {
    Write-Host "AWS Configured: NO" -ForegroundColor Red
}

Write-Host ""
Write-Host "To test sync:" -ForegroundColor Yellow
Write-Host "1. Open Photobooth app"
Write-Host "2. Go to Settings"
Write-Host "3. Enable sync options"
Write-Host "4. Click Sync Now"
