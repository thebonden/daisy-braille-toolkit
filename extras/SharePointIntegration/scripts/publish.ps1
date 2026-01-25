<#
Publish the sample app as a self-contained folder (optional)

Usage:
  pwsh .\scripts\publish.ps1 -OutDir ".\publish"
#>
param(
  [string]$OutDir = ".\publish",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

dotnet publish .\src\DbtSharePointSample\DbtSharePointSample.csproj `
  -c $Configuration `
  -o $OutDir

Write-Host "[DBT] Published to: $OutDir" -ForegroundColor Cyan
