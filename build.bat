@echo off
cd /d %~dp0

echo Files in directory:
dir /b *.cs

echo.
echo Compiling (modular source files)...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:BatteryTray.exe /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Windows.Forms.DataVisualization.dll /r:System.Management.dll /r:System.dll /r:System.Core.dll *.cs
set BUILD_EXIT=%errorlevel%

echo.
echo Exit code: %BUILD_EXIT%
echo.
echo Looking for exe:
dir /b *.exe 2>nul
if not exist BatteryTray.exe echo NO EXE FOUND
if not "%BUILD_EXIT%"=="0" exit /b %BUILD_EXIT%
