@echo off
rem Build UsageTray.exe using the C# compiler bundled with .NET Framework 4.x (no SDK required)
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
cd /d "%~dp0"
"%CSC%" /nologo /target:winexe /optimize+ /out:UsageTray.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  src\*.cs
if errorlevel 1 (echo Build GAGAL & exit /b 1)
echo Build OK: %~dp0UsageTray.exe
