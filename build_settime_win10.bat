@echo off
REM Build SetTime_Desktop.exe for Win10 desktop testing only.
REM Uses standard .NET Framework 3.5 references and kernel32 P/Invoke.
REM This binary is for development verification of parsing/dry-run/MessageBox logic.
REM DO NOT ship this build to Windows CE devices — use build_settime_ce.bat for that.

set CSC=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe

"%CSC%" /target:winexe /define:WIN10_DESKTOP /out:SetTime_Desktop.exe /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll SetTime.cs
