@echo off
chcp 65001 > nul
cd /d "%~dp0"

:: 优先用 pwsh (PowerShell 7+)，没有才用 Windows PowerShell
where pwsh >nul 2>nul
if %ERRORLEVEL% equ 0 (
    pwsh -ExecutionPolicy Bypass -NoProfile -File "pack.ps1" < nul
) else (
    powershell -ExecutionPolicy Bypass -NoProfile -Command "Get-Content '%~dpn0.ps1' -Raw | Invoke-Expression"
)

if errorlevel 1 (
    echo.
    echo 打包失败，按任意键退出...
    pause > nul
)
