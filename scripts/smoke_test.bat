@echo off
setlocal

cd /d %~dp0\..

echo [1/3] Building BatteryTray...
call build.bat
if errorlevel 1 (
  echo Build failed.
  exit /b 1
)

echo [2/3] Verifying executable exists...
if not exist BatteryTray.exe (
  echo BatteryTray.exe not found after build.
  exit /b 1
)

echo [3/3] Basic startup smoke test (5s)...
start "" BatteryTray.exe
powershell -NoProfile -Command "Start-Sleep -Seconds 5"
taskkill /IM BatteryTray.exe /F >nul 2>&1

echo Smoke test complete.
exit /b 0
