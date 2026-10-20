@echo off
cd /d "%~dp0"
echo Encerrando o SafeGuard IA...
taskkill /FI "WINDOWTITLE eq SafeGuard API*" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq SafeGuard Frontend*" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq SafeGuard Motor IA*" /T /F >nul 2>&1
docker compose stop
echo Pronto. Os dados do banco foram mantidos.
timeout /t 4 >nul
