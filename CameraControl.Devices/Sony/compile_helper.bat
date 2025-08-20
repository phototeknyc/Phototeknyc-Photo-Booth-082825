@echo off
echo Compiling Sony SDK Helper DLL...

REM Set up Visual Studio environment
call "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"

REM Set paths
set SONY_SDK_PATH=..\..\sonysdk\external\crsdk
set SONY_SDK_INCLUDE=..\..\sonysdk\app\CRSDK

REM Compile and link with Sony SDK
cl /LD /I%SONY_SDK_INCLUDE% SonySDKHelper.cpp /link /LIBPATH:%SONY_SDK_PATH% Cr_Core.lib /OUT:SonySDKHelper.dll

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Success! Copying DLL to output directories...
    copy SonySDKHelper.dll ..\..\bin\Debug\
    copy SonySDKHelper.dll ..\..\
    echo Done!
) else (
    echo.
    echo Failed to compile. Check that Cr_Core.lib exists in %SONY_SDK_PATH%
    echo.
    echo Trying x64 specific path...
    cl /LD /I%SONY_SDK_INCLUDE% SonySDKHelper.cpp /link /LIBPATH:%SONY_SDK_PATH%\x64 Cr_Core.lib /OUT:SonySDKHelper.dll
    
    if %ERRORLEVEL% EQU 0 (
        echo.
        echo Success with x64 path! Copying DLL to output directories...
        copy SonySDKHelper.dll ..\..\bin\Debug\
        copy SonySDKHelper.dll ..\..\
        echo Done!
    )
)

pause