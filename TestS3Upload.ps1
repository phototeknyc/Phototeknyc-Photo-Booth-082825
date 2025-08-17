# PowerShell script to test S3 upload directly
# Run this in PowerShell to test your S3 configuration

$bucketName = "phototeknyc"
$testFile = "test-gallery.html"
$s3Key = "events/test/test-direct.html"

# Create a simple HTML file
$htmlContent = @"
<!DOCTYPE html>
<html>
<head>
    <title>S3 Upload Test</title>
    <style>
        body {
            font-family: Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
        }
        .success {
            text-align: center;
            padding: 40px;
            background: rgba(255,255,255,0.1);
            border-radius: 20px;
        }
        h1 { font-size: 3em; }
    </style>
</head>
<body>
    <div class='success'>
        <h1>✅ S3 Upload Working!</h1>
        <p>Your S3 bucket is configured correctly for public access.</p>
        <p>URL: https://$bucketName.s3.amazonaws.com/$s3Key</p>
        <p>Time: <script>document.write(new Date());</script></p>
    </div>
</body>
</html>
"@

# Save the HTML file locally
$htmlContent | Out-File -FilePath $testFile -Encoding UTF8

Write-Host "HTML file created: $testFile"

# Get AWS credentials from environment
$accessKey = [System.Environment]::GetEnvironmentVariable("AWS_ACCESS_KEY_ID", "User")
$secretKey = [System.Environment]::GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "User")
$region = [System.Environment]::GetEnvironmentVariable("S3_REGION", "User")
if (-not $region) { $region = "us-east-1" }

if (-not $accessKey -or -not $secretKey) {
    Write-Host "❌ AWS credentials not found in environment variables" -ForegroundColor Red
    Write-Host "Please set AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY as User environment variables" -ForegroundColor Yellow
    exit
}

Write-Host "✅ AWS credentials found" -ForegroundColor Green
Write-Host "Region: $region"
Write-Host "Bucket: $bucketName"

# Install AWS Tools if not present
if (-not (Get-Module -ListAvailable -Name AWS.Tools.S3)) {
    Write-Host "Installing AWS Tools for PowerShell..." -ForegroundColor Yellow
    Install-Module -Name AWS.Tools.S3 -Force -Scope CurrentUser
}

Import-Module AWS.Tools.S3

# Set AWS credentials
Set-AWSCredential -AccessKey $accessKey -SecretKey $secretKey -StoreAs TestProfile

# Upload file to S3
try {
    Write-Host "Uploading to S3..." -ForegroundColor Yellow
    
    Write-S3Object -BucketName $bucketName `
                   -File $testFile `
                   -Key $s3Key `
                   -ProfileName TestProfile `
                   -Region $region `
                   -CannedACLName public-read `
                   -ContentType "text/html"
    
    $url = "https://$bucketName.s3.amazonaws.com/$s3Key"
    
    Write-Host "✅ Upload successful!" -ForegroundColor Green
    Write-Host "URL: $url" -ForegroundColor Cyan
    Write-Host "Opening in browser..." -ForegroundColor Yellow
    
    Start-Process $url
    
} catch {
    Write-Host "❌ Upload failed: $_" -ForegroundColor Red
}

# Clean up
Remove-Item $testFile -Force
Write-Host "Temporary file cleaned up"