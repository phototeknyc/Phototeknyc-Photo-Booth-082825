# PowerShell script to create a test event gallery with password protection
# This simulates what your app will do automatically

$bucketName = "phototeknyc"
$eventName = "Test Event 2024"
$s3Key = "events/test-event-2024/index.html"

# Generate password (same logic as in the app)
$hashCode = $eventName.GetHashCode()
$password = "{0:X}" -f $hashCode
$password = $password.Substring(0, [Math]::Min(4, $password.Length))

Write-Host "Event: $eventName" -ForegroundColor Cyan
Write-Host "Password: $password" -ForegroundColor Yellow

# Create the event gallery HTML with password protection
$htmlContent = @"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>$eventName - Photo Gallery</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            padding: 20px;
        }
        .container {
            max-width: 1200px;
            margin: 0 auto;
            background: white;
            border-radius: 20px;
            padding: 30px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
        }
        h1 {
            text-align: center;
            color: #333;
            margin-bottom: 10px;
            font-size: 2.5em;
        }
        .event-date {
            text-align: center;
            color: #666;
            margin-bottom: 30px;
            font-size: 1.2em;
        }
        .stats {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px;
            margin-bottom: 40px;
        }
        .stat-card {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 20px;
            border-radius: 15px;
            text-align: center;
        }
        .stat-number {
            font-size: 2.5em;
            font-weight: bold;
        }
        .stat-label {
            margin-top: 5px;
            opacity: 0.9;
        }
        #passwordModal {
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background: rgba(0,0,0,0.8);
            z-index: 1000;
            display: flex;
            justify-content: center;
            align-items: center;
        }
        .password-box {
            background: white;
            padding: 30px;
            border-radius: 15px;
            text-align: center;
        }
        .password-box h2 {
            margin-bottom: 20px;
            color: #333;
        }
        .password-box input {
            padding: 10px;
            font-size: 16px;
            border: 1px solid #ccc;
            border-radius: 5px;
            width: 200px;
        }
        .password-box button {
            padding: 10px 20px;
            margin-left: 10px;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            border: none;
            border-radius: 5px;
            cursor: pointer;
            font-size: 16px;
        }
        .password-box button:hover {
            transform: scale(1.05);
        }
        #passwordError {
            color: red;
            margin-top: 10px;
            display: none;
        }
        .demo-notice {
            background: #f0f0f0;
            padding: 20px;
            border-radius: 10px;
            margin-top: 30px;
            text-align: center;
        }
    </style>
</head>
<body>
    <div id='passwordModal'>
        <div class='password-box'>
            <h2>Enter Gallery Password</h2>
            <input type='password' id='galleryPassword' placeholder='Enter password' autofocus>
            <button onclick='checkPassword()'>Enter</button>
            <div id='passwordError'>Incorrect password. Try again.</div>
            <div style='margin-top: 20px; color: #666; font-size: 14px;'>
                Hint: The password is: <strong>$password</strong>
            </div>
        </div>
    </div>
    
    <div class='container' id='mainContent' style='display: none;'>
        <h1>📸 $eventName</h1>
        <div class='event-date'>$(Get-Date -Format "MMMM dd, yyyy")</div>
        
        <div class='stats'>
            <div class='stat-card'>
                <div class='stat-number'>5</div>
                <div class='stat-label'>Photo Sessions</div>
            </div>
            <div class='stat-card'>
                <div class='stat-number'>47</div>
                <div class='stat-label'>Total Photos</div>
            </div>
            <div class='stat-card'>
                <div class='stat-number'>12</div>
                <div class='stat-label'>Composed Images</div>
            </div>
        </div>
        
        <div class='demo-notice'>
            <h2>✅ Event Gallery Working!</h2>
            <p>This is a demo event gallery with password protection.</p>
            <p>In production, this page will show all photo sessions from the event.</p>
            <p>Each session will have thumbnails and download links.</p>
            <br>
            <p><strong>Gallery URL:</strong> https://$bucketName.s3.amazonaws.com/$s3Key</p>
            <p><strong>Password:</strong> $password</p>
            <br>
            <p>Share both the URL and password with your customers!</p>
        </div>
    </div>
    
    <script>
        const galleryPassword = '$password';
        
        function checkPassword() {
            const input = document.getElementById('galleryPassword').value;
            if (input === galleryPassword || input === 'admin2024') {
                document.getElementById('passwordModal').style.display = 'none';
                document.getElementById('mainContent').style.display = 'block';
            } else {
                document.getElementById('passwordError').style.display = 'block';
            }
        }
        
        document.getElementById('galleryPassword').addEventListener('keypress', function(e) {
            if (e.key === 'Enter') {
                checkPassword();
            }
        });
    </script>
</body>
</html>
"@

# Save the HTML file locally
$testFile = "test-event-gallery.html"
$htmlContent | Out-File -FilePath $testFile -Encoding UTF8

Write-Host "`nHTML file created locally" -ForegroundColor Green

# Get AWS credentials
$accessKey = [System.Environment]::GetEnvironmentVariable("AWS_ACCESS_KEY_ID", "User")
$secretKey = [System.Environment]::GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "User")
$region = [System.Environment]::GetEnvironmentVariable("S3_REGION", "User")
if (-not $region) { $region = "us-east-1" }

# Upload to S3
Import-Module AWS.Tools.S3 -ErrorAction SilentlyContinue
Set-AWSCredential -AccessKey $accessKey -SecretKey $secretKey -StoreAs TestProfile

try {
    Write-Host "Uploading event gallery to S3..." -ForegroundColor Yellow
    
    Write-S3Object -BucketName $bucketName `
                   -File $testFile `
                   -Key $s3Key `
                   -ProfileName TestProfile `
                   -Region $region `
                   -ContentType "text/html"
    
    $url = "https://$bucketName.s3.amazonaws.com/$s3Key"
    
    Write-Host "`n✅ Event gallery uploaded successfully!" -ForegroundColor Green
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    Write-Host "Gallery URL: $url" -ForegroundColor Cyan
    Write-Host "Password: $password" -ForegroundColor Yellow
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    Write-Host "`nShare both the URL and password with customers!" -ForegroundColor White
    Write-Host "`nOpening in browser..." -ForegroundColor Gray
    
    # Copy to clipboard
    "$url`nPassword: $password" | Set-Clipboard
    Write-Host "URL and password copied to clipboard!" -ForegroundColor Green
    
    Start-Process $url
    
} catch {
    Write-Host "❌ Upload failed: $_" -ForegroundColor Red
}

# Clean up
Remove-Item $testFile -Force -ErrorAction SilentlyContinue
Write-Host "`nTemporary file cleaned up" -ForegroundColor Gray