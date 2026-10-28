@echo off
rem Builds bin\TapMirror.exe with the .NET Framework 4 compiler that ships with Windows (no SDK needed).
setlocal
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist bin mkdir bin
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /out:bin\TapMirror.exe ^
  /win32icon:TapMirror.ico /win32manifest:app.manifest /resource:TapMirror.ico,TapMirror.ico ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  src\*.cs
if errorlevel 1 exit /b 1
echo built bin\TapMirror.exe
