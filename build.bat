@echo off
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found: %CSC%
  exit /b 1
)

rem csc cannot overwrite a running exe, so stop the tray first.
taskkill /f /im VkM5BatteryTray.exe >nul 2>&1
taskkill /f /im selftest.exe >nul 2>&1
ping -n 2 127.0.0.1 >nul

echo [1/2] VkM5BatteryTray.exe
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Core.dll ^
  /out:VkM5BatteryTray.exe src\VkM5BatteryTray.cs
if errorlevel 1 exit /b 1

echo [2/2] selftest.exe
"%CSC%" /nologo /target:exe /platform:x64 /main:VkM5Battery.SelfTest ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Core.dll ^
  /out:selftest.exe src\VkM5BatteryTray.cs tools\SelfTest.cs
if errorlevel 1 exit /b 1

rem Anything created inside a Low-integrity sandbox directory inherits that
rem label, and a Low-IL process may not add a notification icon to the tray.
rem The shell silently drops the icon in that case, so label the binaries.
icacls "VkM5BatteryTray.exe" /setintegritylevel Medium >nul 2>&1
icacls "selftest.exe" /setintegritylevel Medium >nul 2>&1

echo.
echo Done. Run: VkM5BatteryTray.exe    Check: selftest.exe 3
