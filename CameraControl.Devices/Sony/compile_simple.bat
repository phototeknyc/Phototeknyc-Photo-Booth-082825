@echo off
echo Compiling Simple Sony SDK Helper DLL...

REM Set up Visual Studio environment for x64
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"

REM Compile the simple helper (no Sony SDK linking required)
cl /LD /MD SonySDKHelperSimple.cpp /Fe:SonySDKHelper.dll

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
    echo Compilation failed.
)

pause