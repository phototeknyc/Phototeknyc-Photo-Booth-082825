@echo off
echo Building Sony SDK Helper DLL...

REM Set up Visual Studio environment
call "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"

REM Compile the helper DLL
cl /LD /MD SonySDKHelper.cpp /link /OUT:SonySDKHelper.dll Cr_Core.lib /LIBPATH:..\..\sonysdk\app\CrSDK\app\x64

if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b 1
)

echo Build successful!
echo Copying to output directories...

REM Copy to Debug output
copy SonySDKHelper.dll ..\..\bin\Debug\
copy SonySDKHelper.dll ..\..\

echo Done!
pause