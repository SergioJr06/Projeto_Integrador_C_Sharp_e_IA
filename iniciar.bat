@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title SafeGuard IA - Iniciador
echo ============================================
echo   SafeGuard IA - iniciando o sistema
echo ============================================
echo.

echo [1/6] Verificando requisitos...
where docker >nul 2>&1 || (echo [ERRO] Docker nao encontrado. Instale o Docker Desktop. & goto erro)
where dotnet >nul 2>&1 || (echo [ERRO] .NET nao encontrado. Instale o SDK .NET 10. & goto erro)
dotnet --list-sdks 2>nul | findstr /b "10." >nul || (
  echo [ERRO] O SDK .NET 10 nao esta instalado. Pode haver so o runtime.
  echo        Baixe o SDK em https://dotnet.microsoft.com/download/dotnet/10.0
  echo        Instale, feche este terminal e rode de novo.
  goto erro
)
set "PY="
for %%V in (3.12 3.11 3.10) do if not defined PY py -%%V -c "import sys" >nul 2>&1 && set "PY=py -%%V"
if not defined PY py -3 -c "import sys" >nul 2>&1 && set "PY=py -3"
if not defined PY python -c "import sys" >nul 2>&1 && set "PY=python"
if not defined PY (
  echo [ERRO] Nenhum Python funcional foi encontrado.
  echo        Instale o Python 3.12 em python.org e marque "Add python.exe to PATH".
  echo        Se ja instalou, desative o atalho da Microsoft Store em:
  echo        Configuracoes - Aplicativos - Configuracoes avancadas - Aliases de execucao de aplicativo.
  goto erro
)
echo       Python encontrado: %PY%
docker info >nul 2>&1 || (echo [ERRO] O Docker Desktop nao esta aberto. Abra-o, espere iniciar e rode de novo. & goto erro)

echo [2/6] Preparando o ambiente Python...
if not exist ".venv\Scripts\python.exe" (
  echo       Criando ambiente virtual...
  %PY% -m venv .venv || goto erro
)
if not exist ".venv\.instalado" (
  echo       Instalando dependencias, na primeira vez demora varios minutos...
  ".venv\Scripts\python.exe" -m pip install --upgrade pip || goto erro
  ".venv\Scripts\python.exe" -m pip install -r requirements.txt || goto erro
  echo ok> ".venv\.instalado"
)

echo [3/6] Subindo o banco de dados PostgreSQL...
docker compose up -d || goto erro
set /a tentativas=0
:espera_banco
docker compose exec -T db pg_isready -U admin -d safeguard_db >nul 2>&1 && goto banco_ok
set /a tentativas+=1
if %tentativas% GEQ 30 (echo [ERRO] O banco nao ficou pronto a tempo. & goto erro)
timeout /t 2 /nobreak >nul
goto espera_banco
:banco_ok
echo       Banco pronto.

set ASPNETCORE_ENVIRONMENT=Development
echo [4/6] Iniciando a API, porta 5168...
start "SafeGuard API" cmd /k dotnet run --project SafeGuardBackend\SafeGuardBackend.csproj --launch-profile http

echo [5/6] Iniciando o painel, porta 5080...
start "SafeGuard Frontend" cmd /k dotnet run --project SafeGuardFrontend\SafeGuardFrontend.csproj --urls http://0.0.0.0:5080

echo.
choice /c SN /t 10 /d S /m "Iniciar tambem o motor de IA com a webcam"
if errorlevel 2 goto sem_ia
echo       Iniciando o motor de IA...
start "SafeGuard Motor IA" cmd /k ".venv\Scripts\python.exe src\automacaototal.py"
:sem_ia

echo [6/6] Aguardando o painel ficar disponivel...
set /a tentativas=0
:espera_painel
curl -s -o nul http://localhost:5080/login && goto abrir
set /a tentativas+=1
if %tentativas% GEQ 90 (echo [AVISO] O painel demorou. Abra http://localhost:5080 manualmente. & goto fim)
timeout /t 2 /nobreak >nul
goto espera_painel
:abrir
start "" http://localhost:5080

:fim
echo.
echo Tudo iniciado. Login: admin / SafeGuard@2026
echo.
echo No celular, na mesma rede Wi-Fi, abra um destes enderecos:
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do echo    http://%%a:5080
echo Se nao abrir, rode liberar_celular.bat como administrador, uma unica vez.
echo.
echo Para encerrar, rode parar.bat
timeout /t 8 >nul
exit /b 0

:erro
echo.
echo O inicio foi interrompido. Corrija o problema acima e rode de novo.
pause
exit /b 1
