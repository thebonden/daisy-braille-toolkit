[CmdletBinding()]
param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [switch]$SelfContained = $true,
  [switch]$SingleFile = $false
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".."))
$project = Join-Path $repoRoot "DAISY-Braille Toolkit\DAISY-Braille Toolkit.csproj"

if (-not (Test-Path $project)) {
  throw "Could not find csproj at: $project"
}

$distRoot = Join-Path $repoRoot "dist"
$publishDir = Join-Path $distRoot "$Runtime"

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$props = @()
if ($SingleFile) {
  $props += "/p:PublishSingleFile=true"
  $props += "/p:IncludeAllContentForSelfExtract=true"
}

$sc = if ($SelfContained) { "true" } else { "false" }

Write-Host "Publishing..." -ForegroundColor Cyan
Write-Host "  Project: $project"
Write-Host "  Output : $publishDir"
Write-Host "  Runtime: $Runtime"
Write-Host "  SelfContained: $sc"
Write-Host "  SingleFile   : $($SingleFile.IsPresent)"

& dotnet publish "$project" -c $Configuration -r $Runtime --self-contained $sc -o "$publishDir" @props

# Create a zip you can share/test
$zipPath = Join-Path $distRoot "DaisyBrailleToolkit-$Runtime.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

Write-Host "Creating zip: $zipPath" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

Write-Host "Done." -ForegroundColor Green
Write-Host "Publish folder: $publishDir"
Write-Host "Zip file     : $zipPath"
