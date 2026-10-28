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
rem checksums for the release page, in the "<sha256>  <name>" format of sha256sum, with LF line ends so "sha256sum -c" works
powershell -NoProfile -Command "$l = Get-FileHash '%ROOT%dist\TapMirror.apk','%ROOT%dist\TapMirror.exe' -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLower() + '  ' + (Split-Path $_.Path -Leaf) }; [IO.File]::WriteAllText('%ROOT%dist\SHA256SUMS.txt', ($l -join \"`n\") + \"`n\")"
echo built dist\TapMirror.exe, dist\TapMirror.apk and dist\SHA256SUMS.txt
