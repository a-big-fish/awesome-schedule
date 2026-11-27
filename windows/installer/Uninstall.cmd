@echo off
set "AWESOME_DEMO_INSTALL=%~dp0"
echo Close Awesome Schedule before uninstalling.
echo Demo data will be kept under LOCALAPPDATA\AwesomeInterviewNativeDemo.
set /p answer=Type YES to uninstall: 
if not "%answer%"=="YES" exit /b
powershell.exe -NoProfile -Command "$ErrorActionPreference='Stop'; $dest=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\AwesomeInterviewNativeDemo')); $here=[IO.Path]::GetFullPath($env:AWESOME_DEMO_INSTALL).TrimEnd('\'); if($here -ne $dest){throw 'Unexpected install path'}; if(!(Test-Path (Join-Path $dest 'AwesomeInterview.NativeDemo.exe'))){throw 'Missing installation marker'}; $programs=[Environment]::GetFolderPath('Programs'); $exe=Join-Path $dest 'AwesomeInterview.NativeDemo.exe'; $shell=New-Object -ComObject WScript.Shell; foreach($name in @('Awesome Schedule.lnk','Awesome Interview Native Demo.lnk')){ $link=Join-Path $programs $name; if((Test-Path -LiteralPath $link) -and $shell.CreateShortcut($link).TargetPath -eq $exe){Remove-Item -LiteralPath $link} }; Remove-Item -LiteralPath $dest -Recurse -Force; Write-Host 'Uninstalled'"
if errorlevel 1 pause
