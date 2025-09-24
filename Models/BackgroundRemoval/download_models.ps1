# PowerShell script to download ONNX models for background removal

Write-Host "Downloading Background Removal Models..." -ForegroundColor Green

# Create models directory if it doesn't exist
$modelsPath = $PSScriptRoot
if (!(Test-Path $modelsPath)) {
    New-Item -ItemType Directory -Path $modelsPath -Force | Out-Null
}

# Model URLs
$models = @(
    @{
        Name = "u2net.onnx"
        Url = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2net.onnx"
        Description = "High-quality U2-Net model for photo capture"
    },
    @{
        Name = "u2netp.onnx"
        Url = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx"
        Description = "Lightweight U2-Net model for live view processing"
    },
    @{
        Name = "rvm_mobilenetv3_fp32.onnx"
        Url = "https://github.com/PeterL1n/RobustVideoMatting/releases/download/v1.0.0/rvm_mobilenetv3_fp32.onnx"
        Description = "Robust Video Matting (MobileNetV3) model for streaming live-view background removal"
    }
)

# Download each model
foreach ($model in $models) {
    $outputPath = Join-Path $modelsPath $model.Name

    if (Test-Path $outputPath) {
        Write-Host "Model $($model.Name) already exists, skipping..." -ForegroundColor Yellow
        continue
    }

    Write-Host "Downloading $($model.Name)..." -ForegroundColor Cyan
    Write-Host "  Description: $($model.Description)" -ForegroundColor Gray

    try {
        # Use Invoke-WebRequest for simpler downloading
        Write-Host "  Starting download from: $($model.Url)" -ForegroundColor Gray

        # Download the file
        Invoke-WebRequest -Uri $model.Url -OutFile $outputPath -UseBasicParsing

        # Verify download
        if (Test-Path $outputPath) {
            Write-Host "  Successfully downloaded $($model.Name)" -ForegroundColor Green

            # Verify file size
            $fileInfo = Get-Item $outputPath
            $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
            Write-Host "  File size: $sizeMB MB" -ForegroundColor Gray
        }
        else {
            throw "Download failed - file not found after download"
        }
    }
    catch {
        Write-Host "  Error downloading $($model.Name): $_" -ForegroundColor Red
        if (Test-Path $outputPath) {
            Remove-Item $outputPath -Force
        }
    }
}

# Download sample backgrounds
Write-Host "`nCreating sample virtual backgrounds..." -ForegroundColor Green

$backgroundsPath = Join-Path (Split-Path $modelsPath -Parent) "Backgrounds"
$categories = @("Solid", "Gradient", "Nature", "Office", "Abstract", "Custom")

foreach ($category in $categories) {
    $categoryPath = Join-Path $backgroundsPath $category
    if (!(Test-Path $categoryPath)) {
        New-Item -ItemType Directory -Path $categoryPath -Force | Out-Null
        Write-Host "  Created category: $category" -ForegroundColor Gray
    }
}

# Create some solid color backgrounds
$solidPath = Join-Path $backgroundsPath "Solid"
$colors = @(
    @{Name="White"; Color="#FFFFFF"},
    @{Name="Black"; Color="#000000"},
    @{Name="Gray"; Color="#808080"},
    @{Name="Blue"; Color="#0066CC"},
    @{Name="Green"; Color="#00AA00"},
    @{Name="Red"; Color="#CC0000"}
)

Add-Type -AssemblyName System.Drawing

foreach ($color in $colors) {
    $outputFile = Join-Path $solidPath "$($color.Name).png"
    if (!(Test-Path $outputFile)) {
        $bitmap = New-Object System.Drawing.Bitmap(1920, 1080)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($color.Color))
        $graphics.FillRectangle($brush, 0, 0, 1920, 1080)
        $bitmap.Save($outputFile, [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose()
        $bitmap.Dispose()
        $brush.Dispose()
        Write-Host "  Created solid background: $($color.Name)" -ForegroundColor Gray
    }
}

Write-Host "`nModel setup completed!" -ForegroundColor Green
Write-Host "Models location: $modelsPath" -ForegroundColor Cyan
Write-Host "Backgrounds location: $backgroundsPath" -ForegroundColor Cyan
