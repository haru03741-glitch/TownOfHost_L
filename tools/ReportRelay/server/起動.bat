@echo off
chcp 65001 >nul
title TOHL 報告の鯖 - 止めるときはこの窓を閉じる
cd /d "%~dp0"

rem ==== Node.js: 無ければ入れる ====
where node >nul 2>&1
if not errorlevel 1 goto node_ok
echo Node.js が入っていないので入れます。少し時間がかかります...
winget install -e --id OpenJS.NodeJS.LTS --accept-package-agreements --accept-source-agreements
echo.
echo 入れ終わったら、この窓を閉じて もう一度 起動.bat を押してください
pause
exit /b 1

:node_ok
for /f %%v in ('node -p "process.versions.node.split('.')[0]"') do set NODE_MAJOR=%%v
if %NODE_MAJOR% GEQ 20 goto settings
echo Node.js が古いです。今は %NODE_MAJOR%、20 以上にしてください: winget upgrade OpenJS.NodeJS.LTS
pause
exit /b 1

rem ==== 設定: 無ければ見本から作って開く ====
:settings
if exist "設定.json" goto tunnel
copy "設定の見本.json" "設定.json" >nul
echo 設定.json を作りました。メモ帳で開くので、bot のトークンなどを書いて保存し、もう一度 起動.bat を押してください
start "" notepad "設定.json"
pause
exit /b 0

rem ==== Cloudflare のトンネル: 入っていれば動かす ====
:tunnel
sc query cloudflared >nul 2>&1
if errorlevel 1 goto no_tunnel
sc query cloudflared | find "RUNNING" >nul
if not errorlevel 1 goto tunnel_ok
echo トンネルが止まっているので起動します...
net start cloudflared >nul 2>&1
sc query cloudflared | find "RUNNING" >nul
if not errorlevel 1 goto tunnel_ok
echo [!] トンネルを起動できませんでした。この bat を右クリック → 管理者として実行 してください
goto run
:tunnel_ok
echo トンネル: 動いています
goto run
:no_tunnel
echo [!] Cloudflare のトンネル cloudflared が入っていません。外からは届きません

rem ==== 鯖: 落ちたら立ち上げ直す ====
:run
echo.
node server.mjs
set CODE=%errorlevel%
if "%CODE%"=="2" goto bad_settings
if "%CODE%"=="3" goto stop
echo.
echo [%date% %time%] 鯖が止まりました。5 秒後に立ち上げ直します。やめるならこの窓を閉じる
timeout /t 5 >nul
goto run

:bad_settings
echo.
echo 設定.json を直してください。メモ帳で開きます。直したら もう一度 起動.bat を押してください
start "" notepad "設定.json"
pause
exit /b 2

:stop
pause
exit /b 3
