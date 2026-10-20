@echo off
net session >nul 2>&1 || (
  echo Clique com o botao direito neste arquivo e escolha "Executar como administrador".
  pause
  exit /b 1
)
netsh advfirewall firewall delete rule name="SafeGuard Painel" >nul 2>&1
netsh advfirewall firewall add rule name="SafeGuard Painel" dir=in action=allow protocol=TCP localport=5080 profile=private
echo.
echo Porta 5080 liberada para redes privadas. Agora o celular pode acessar o painel.
pause
