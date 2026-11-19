@echo off
chcp 65001 >nul
set "EXE=%~dp0runtime\v20\TiaMcpServer.exe"
if not exist "%EXE%" set "EXE=%~dp0tools\tiaportal-mcp\src\TiaMcpServer\bin-v20\Release\net48\TiaMcpServer.exe"
if not exist "%EXE%" (
  echo [Error] TIA Portal V20 engine not found. See GitHub Releases for the V20 bundle.
  exit /b 2
)
"%EXE%" %*
exit /b %ERRORLEVEL%
