@echo off
setlocal enabledelayedexpansion

REM DBT SharePoint provisioning helper
REM Requires PowerShell 7 (pwsh) + PnP.PowerShell module

where pwsh >nul 2>&1
if errorlevel 1 (
  echo.
  echo PowerShell 7 (pwsh) was not found.
  echo Install it from Microsoft Store or https://learn.microsoft.com/powershell/
  echo.
  pause
  exit /b 1
)

set "SITE="
set /p SITE=Enter SharePoint site URL (e.g. https://tenant.sharepoint.com/sites/YourSite): 
if "%SITE%"=="" (
  echo No site URL entered.
  exit /b 1
)

echo.
echo Running provisioning script...
echo Site: %SITE%
echo.

pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-DBTSharePoint.ps1" -SiteUrl "%SITE%"

echo.
echo Done.
pause
