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

# Build the solution
"$MSBUILD_PATH" Photobooth.sln /p:Configuration=Debug /v:minimal

# Check if build succeeded
if [ $? -eq 0 ]; then
    echo ""
    echo "Build completed successfully!"
else
    echo ""
    echo "Build failed with error code $?"
fi