@echo off
setlocal

REM Double-click to publish a Release build (win-x64) and create dist\DaisyBrailleToolkit-win-x64.zip

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-win-x64.ps1" -Configuration Release -Runtime win-x64

pause
