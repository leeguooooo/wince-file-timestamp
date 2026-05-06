@echo off
REM Build SetTime.exe for Windows CE / .NET Compact Framework 3.5.
REM Install Visual Studio 2008 and the .NET Compact Framework 3.5 SDK first.
REM If your SDK is installed elsewhere, edit CFSDK below.

set CSC=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe
set CFSDK=C:\Program Files\Microsoft.NET\SDK\CompactFramework\v3.5\WindowsCE

"%CSC%" /noconfig /nostdlib /target:winexe /out:SetTime.exe /r:"%CFSDK%\mscorlib.dll" /r:"%CFSDK%\System.dll" /r:"%CFSDK%\System.Windows.Forms.dll" /r:"%CFSDK%\System.Drawing.dll" SetTime.cs
