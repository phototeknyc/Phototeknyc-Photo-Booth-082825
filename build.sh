#!/bin/bash

echo "Building Photobooth Solution..."
echo ""

# Set the path to MSBuild
MSBUILD_PATH="/mnt/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/amd64/MSBuild.exe"

# Check if MSBuild exists
if [ ! -f "$MSBUILD_PATH" ]; then
    echo "Error: MSBuild not found at $MSBUILD_PATH"
    echo "Please install Visual Studio 2022 or update the path in this script."
    exit 1
fi

# Restore NuGet packages
echo "Restoring NuGet packages..."
"$MSBUILD_PATH" Photobooth.sln /t:Restore /p:Configuration=Debug /v:minimal

# Build the solution
echo "Building solution..."
"$MSBUILD_PATH" Photobooth.sln /p:Configuration=Debug /v:minimal

# Check if build succeeded
BUILD_RESULT=$?
if [ $BUILD_RESULT -eq 0 ]; then
    echo ""
    echo "Build completed successfully!"
    
    # Copy Canon SDK files to bin directory
    echo "Copying Canon SDK files..."
    BIN_DIR="bin/Debug"
    
    if [ -f "EDSDK.dll" ]; then
        cp "EDSDK.dll" "$BIN_DIR/" 2>/dev/null || echo "Warning: Could not copy EDSDK.dll"
        echo "  ✓ EDSDK.dll copied"
    else
        echo "  ⚠ EDSDK.dll not found in root directory"
    fi
    
    if [ -f "EdsImage.dll" ]; then
        cp "EdsImage.dll" "$BIN_DIR/" 2>/dev/null || echo "Warning: Could not copy EdsImage.dll"
        echo "  ✓ EdsImage.dll copied"
    else
        echo "  ⚠ EdsImage.dll not found in root directory"
    fi
    
    # Copy AWS SDK files
    echo "Copying AWS SDK files..."
    if [ -f "packages/AWSSDK.Core.4.0.0.22/lib/net472/AWSSDK.Core.dll" ]; then
        cp "packages/AWSSDK.Core.4.0.0.22/lib/net472/AWSSDK.Core.dll" "$BIN_DIR/"
        echo "  ✓ AWSSDK.Core.dll copied"
    else
        echo "  ⚠ AWSSDK.Core.dll not found"
    fi
    
    if [ -f "packages/AWSSDK.S3.4.0.0/lib/net472/AWSSDK.S3.dll" ]; then
        cp "packages/AWSSDK.S3.4.0.0/lib/net472/AWSSDK.S3.dll" "$BIN_DIR/"
        echo "  ✓ AWSSDK.S3.dll copied"
    else
        echo "  ⚠ AWSSDK.S3.dll not found"
    fi
    
    # Copy FFmpeg if available
    echo "Checking for FFmpeg..."
    FFMPEG_COPIED=false
    
    # Check common FFmpeg locations
    FFMPEG_LOCATIONS=(
        "ffmpeg.exe"
        "bin/ffmpeg.exe"
        "tools/ffmpeg.exe"
        "/mnt/c/ffmpeg/bin/ffmpeg.exe"
        "/mnt/c/Program Files/ffmpeg/bin/ffmpeg.exe"
    )
    
    for ffmpeg_path in "${FFMPEG_LOCATIONS[@]}"; do
        if [ -f "$ffmpeg_path" ]; then
            cp "$ffmpeg_path" "$BIN_DIR/" 2>/dev/null || echo "Warning: Could not copy ffmpeg.exe"
            echo "  ✓ ffmpeg.exe copied from $ffmpeg_path"
            FFMPEG_COPIED=true
            break
        fi
    done
    
    if [ "$FFMPEG_COPIED" = false ]; then
        echo "  ⚠ ffmpeg.exe not found in common locations"
        echo "    Video generation features may not work without FFmpeg"
        echo "    Download from: https://ffmpeg.org/download.html"
        echo "    Place ffmpeg.exe in the project root or bin/Debug folder"
    fi

    # Copy ONNX models for background removal
    echo "Copying ONNX models for background removal..."
    MODELS_SOURCE_DIR="Models/BackgroundRemoval"
    MODELS_DEST_DIR="$BIN_DIR/Models/BackgroundRemoval"

    # Create destination directory if it doesn't exist
    if [ ! -d "$MODELS_DEST_DIR" ]; then
        mkdir -p "$MODELS_DEST_DIR"
        echo "  Created models directory: $MODELS_DEST_DIR"
    fi

    # Copy all ONNX models for background removal
    MODEL_COUNT=0
    for model_file in "$MODELS_SOURCE_DIR"/*.onnx; do
        if [ -f "$model_file" ]; then
            filename=$(basename "$model_file")
            cp "$model_file" "$MODELS_DEST_DIR/" 2>/dev/null || echo "Warning: Could not copy $filename"

            # Get file size in MB
            size_bytes=$(stat -c%s "$model_file" 2>/dev/null || stat -f%z "$model_file" 2>/dev/null)
            size_mb=$((size_bytes / 1048576))

            # Special messages for known models
            case "$filename" in
                "u2net.onnx")
                    echo "  ✓ u2net.onnx copied ($size_mb MB)"
                    ;;
                "u2netp.onnx")
                    echo "  ✓ u2netp.onnx copied ($size_mb MB)"
                    ;;
                "modnet.onnx")
                    echo "  ✓ modnet.onnx copied ($size_mb MB) - Fast human segmentation"
                    ;;
                "pp_humanseg_lite.onnx")
                    echo "  ✓ pp_humanseg_lite.onnx copied ($size_mb MB) - Ultra-fast"
                    ;;
                "rmbg-1.4.onnx")
                    echo "  ✓ rmbg-1.4.onnx copied ($size_mb MB) - Modern AI"
                    ;;
                "selfie_segmentation.onnx")
                    echo "  ✓ selfie_segmentation.onnx copied ($size_mb MB) - Portrait optimized"
                    ;;
                *)
                    echo "  ✓ $filename copied ($size_mb MB)"
                    ;;
            esac
            MODEL_COUNT=$((MODEL_COUNT + 1))
        fi
    done

    if [ $MODEL_COUNT -eq 0 ]; then
        echo "  ⚠ No ONNX models found in $MODELS_SOURCE_DIR"
        echo "    Background removal will use fallback method"
    fi

    echo ""
    echo "✅ Build and dependency copy completed!"
    echo "Output directory: $BIN_DIR"
    
else
    echo ""
    echo "❌ Build failed with error code $BUILD_RESULT"
fi