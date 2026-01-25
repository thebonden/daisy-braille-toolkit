<#
DBT convenience setup script (Windows)
- Provisions SharePoint lists (optional)
- Builds & runs the sample app

Usage:
  pwsh .\scripts\setup.ps1 -SiteUrl "https://<tenant>.sharepoint.com/sites/<SiteName>" -Provision
#>
param(
  [string]$SiteUrl,
  [switch]$Provision
)

$ErrorActionPreference = "Stop"

if ($Provision) {
  if (-not $SiteUrl) { throw "Provide -SiteUrl when using -Provision" }
  Write-Host "[DBT] Provisioning SharePoint lists..." -ForegroundColor Cyan
  pwsh .\provisioning\scripts\Install-DBTSharePoint.ps1 -SiteUrl $SiteUrl
}

Write-Host "[DBT] Building sample app..." -ForegroundColor Cyan
dotnet restore .\DBT.sln
dotnet build .\DBT.sln -c Debug

Write-Host "[DBT] Run the sample app from: src\DbtSharePointSample" -ForegroundColor Cyan
