@echo off
rem Builds dist\TapMirror.exe and dist\TapMirror.apk.
rem The APK is a signed release if android\keystore.properties exists, otherwise a debug build.
setlocal
set ROOT=%~dp0
if not exist "%ROOT%dist" mkdir "%ROOT%dist"

call "%ROOT%windows\build.cmd" || exit /b 1
copy /y "%ROOT%windows\bin\TapMirror.exe" "%ROOT%dist\TapMirror.exe" >nul

cd /d "%ROOT%android"
if exist keystore.properties (
  call "%ROOT%android\gradlew.bat" --quiet assembleRelease || exit /b 1
  copy /y app\build\outputs\apk\release\app-release.apk "%ROOT%dist\TapMirror.apk" >nul
) else (
  call "%ROOT%android\gradlew.bat" --quiet assembleDebug || exit /b 1
  copy /y app\build\outputs\apk\debug\app-debug.apk "%ROOT%dist\TapMirror.apk" >nul
)
echo built dist\TapMirror.exe and dist\TapMirror.apk
