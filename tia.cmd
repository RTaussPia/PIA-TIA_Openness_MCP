@echo off
chcp 65001 >nul
set "EXE=%~dp0runtime\v21\TiaMcpServer.exe"
if not exist "%EXE%" set "EXE=%~dp0tools\tiaportal-mcp\src\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe"
if not exist "%EXE%" (
  echo [Error] TIA Portal V21 engine not found.
  exit /b 2
)
"%EXE%" %*
exit /b %ERRORLEVEL%
