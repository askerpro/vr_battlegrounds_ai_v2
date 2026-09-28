@echo off
rem Запуск выделенного сервера с логом в Logs\ — см. Start-Server.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Server.ps1" %*
