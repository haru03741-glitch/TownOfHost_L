@echo off
chcp 65001 >nul
rem Windows にサインインしたら 起動.bat が勝手に動くよう、スタートアップにショートカットを置く。もう一度押すと外す
set "LINK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\TOHL 報告の鯖.lnk"
set "TARGET=%~dp0起動.bat"
set "WORKDIR=%~dp0"
if exist "%LINK%" goto remove

powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut($env:LINK); $s.TargetPath=$env:TARGET; $s.WorkingDirectory=$env:WORKDIR; $s.Save()"
echo PC の起動で 起動.bat が動くようにしました。もう一度押すと外します
pause
exit /b 0

:remove
del "%LINK%"
echo PC の起動で動かすのをやめました
pause
