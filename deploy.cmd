@echo off
rem Gera o pacote de distribuicao do TravelCut Studio em dist\ (ver deploy.ps1).
rem Uso: deploy.cmd [-SemTestes] [-SemZip] [-FrameworkDependent]
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy.ps1" %*
set "CODE=%ERRORLEVEL%"
rem Aberto por duplo clique: mantem a janela para ler o resultado.
echo %CMDCMDLINE% | find /i "%~nx0" >nul && pause
exit /b %CODE%
