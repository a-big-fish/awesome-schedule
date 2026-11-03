@echo off
set "AWESOME_DEMO_INSTALL=%~dp0"
echo Close Awesome Interview Native Demo before uninstalling.
echo Demo data will be kept under LOCALAPPDATA\AwesomeInterviewNativeDemo.
set /p answer=Type YES to uninstall: 
if not "%answer%"=="YES" exit /b
powershell.exe -NoProfile -Command "$ErrorActionPreference='Stop'; $dest=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\AwesomeInterviewNativeDemo')); $here=[IO.Path]::GetFullPath($env:AWESOME_DEMO_INSTALL).TrimEnd('\'); if($here -ne $dest){throw 'Unexpected install path'}; if(!(Test-Path (Join-Path $dest 'AwesomeInterview.NativeDemo.exe'))){throw 'Missing installation marker'}; $link=Join-Path ([Environment]::GetFolderPath('Programs')) 'Awesome Interview Native Demo.lnk'; if(Test-Path $link){Remove-Item -LiteralPath $link}; Remove-Item -LiteralPath $dest -Recurse -Force; Write-Host 'Uninstalled'"
if errorlevel 1 pause
