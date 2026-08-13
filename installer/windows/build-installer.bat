@echo off
REM DriveLab - atalho de duplo-clique pro build do instalador (roda o PowerShell sem barreira de policy).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" %*
echo.
pause
