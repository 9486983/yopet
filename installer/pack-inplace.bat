@echo off
chcp 65001 > nul
cd /d "%~dp0"

where pwsh >nul 2>nul
if %ERRORLEVEL% equ 0 (
    pwsh -ExecutionPolicy Bypass -NoProfile -File "pack.ps1" -InPlace %* < nul
) else (
    powershell -ExecutionPolicy Bypass -NoProfile -File "pack.ps1" -InPlace %*
)

if errorlevel 1 (
    echo.
    echo 打包失败，按任意键退出...
    pause > nul
)
