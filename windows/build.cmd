@echo off
rem Build UsageTray.exe using the C# compiler bundled with .NET Framework 4.x (no SDK required)
rem
rem   build.cmd            build only
rem   build.cmd install    build, enable auto-start at Windows login, and run UsageTray
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
cd /d "%~dp0"

set INSTALL=0
if /i "%~1"=="install" set INSTALL=1

if "%INSTALL%"=="1" (
  rem A running UsageTray.exe locks the file, so stop it before rebuilding
  taskkill /im UsageTray.exe /f >nul 2>&1
  ping -n 2 127.0.0.1 >nul
)

"%CSC%" /nologo /target:winexe /optimize+ /out:UsageTray.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  src\*.cs
if errorlevel 1 (echo Build GAGAL & exit /b 1)
echo Build OK: %~dp0UsageTray.exe

if not "%INSTALL%"=="1" exit /b 0

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v UsageTray /t REG_SZ /d "\"%~dp0UsageTray.exe\"" /f >nul
if errorlevel 1 (echo Gagal mengaktifkan auto-start & exit /b 1)
echo Auto-start aktif: UsageTray berjalan otomatis setiap login Windows.

start "" "%~dp0UsageTray.exe"
echo UsageTray dijalankan. Cek icon di system tray (menu panah ^^ jika tersembunyi).
exit /b 0
