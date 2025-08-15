@echo off
echo Building Cloud Service Assembly...

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set REFERENCES=/r:bin\Debug\Photobooth.exe /r:packages\AWSSDK.Core.4.0.0.22\lib\net472\AWSSDK.Core.dll /r:packages\AWSSDK.S3.4.0.0\lib\net472\AWSSDK.S3.dll /r:packages\Twilio.7.12.0\lib\net462\Twilio.dll /r:packages\QRCoder.1.4.3\lib\net40\QRCoder.dll /r:System.Drawing.dll /r:WindowsBase.dll /r:PresentationCore.dll /r:System.Net.Http.dll

%CSC% /target:library /out:bin\Debug\CloudService.dll Services\CloudShareService.cs %REFERENCES%

if %ERRORLEVEL% == 0 (
    echo Cloud Service built successfully!
) else (
    echo Failed to build Cloud Service
)