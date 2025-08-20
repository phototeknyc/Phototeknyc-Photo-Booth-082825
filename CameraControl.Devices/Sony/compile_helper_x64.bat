@echo off
echo Compiling Sony SDK Helper DLL for x64...

REM Set up Visual Studio environment for x64
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"

REM Set paths
set SONY_SDK_LIB=..\..\sonysdk\external\crsdk
set SONY_SDK_INCLUDE=..\..\sonysdk\app\CRSDK

echo Include path: %SONY_SDK_INCLUDE%
echo Library path: %SONY_SDK_LIB%

REM Compile for x64 and link with Sony SDK
cl /LD /MD /I%SONY_SDK_INCLUDE% SonySDKHelper.cpp /link /MACHINE:X64 /LIBPATH:%SONY_SDK_LIB% Cr_Core.lib /OUT:SonySDKHelper.dll

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Success! Copying DLL to output directories...
    copy SonySDKHelper.dll ..\..\bin\Debug\
    copy SonySDKHelper.dll ..\..\
    copy SonySDKHelper.dll ..\..\..\
    echo Done!
    echo.
    echo SonySDKHelper.dll has been built successfully!
) else (
    echo.
    echo Compilation failed. Error details above.
)

pause