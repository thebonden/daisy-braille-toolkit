param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$ProjectPath = "DAISY-Braille Toolkit",
    [string]$OutputDir = "publish",
    [string]$WixBin = "C:\Program Files (x86)\WiX Toolset v3.11\bin"
)

# run from repository root (script located in Installer)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
$projectDir = Join-Path $repoRoot $ProjectPath
$publishDir = Join-Path $projectDir $OutputDir

Write-Host "Publishing project '$ProjectPath' -> runtime $Runtime"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir | Out-Null

dotnet publish "$projectDir" -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$publishDir"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Locate WiX tools (allow either explicit path or rely on PATH)
$heat = Join-Path $WixBin "heat.exe"
$candle = Join-Path $WixBin "candle.exe"
$light = Join-Path $WixBin "light.exe"
if (!(Test-Path $heat)) { $heat = "heat.exe" }
if (!(Test-Path $candle)) { $candle = "candle.exe" }
if (!(Test-Path $light)) { $light = "light.exe" }

Write-Host "Harvesting published files with heat"
$harvested = Join-Path $scriptDir "HarvestedComponents.wxs"
& $heat dir "$publishDir" -srd -cg ProductComponents -dr INSTALLFOLDER -var var.PublishDir -out "$harvested"
if ($LASTEXITCODE -ne 0) { throw "heat failed" }

# Determine product version from .csproj (fallback 0.1.0)
$csprojFile = Get-ChildItem -Path $projectDir -Filter *.csproj | Select-Object -First 1
$productVersion = "0.1.0"
if ($csprojFile) {
    try {
        [xml]$projXml = Get-Content $csprojFile.FullName
        $versionNode = $projXml.Project.PropertyGroup.Version
        if ($versionNode) { $productVersion = $versionNode.Trim() }
    } catch {
        Write-Host "Could not read version from csproj, using default $productVersion"
    }
}

Write-Host "Product version: $productVersion"

Write-Host "Compiling .wxs files with candle"
& $candle -dPublishDir="$publishDir" -dProductVersion="$productVersion" -o "$scriptDir\HarvestedComponents.wixobj" "$harvested"
if ($LASTEXITCODE -ne 0) { throw "candle failed for harvested components" }

& $candle -dPublishDir="$publishDir" -dProductVersion="$productVersion" -o "$scriptDir\Product.wixobj" "$scriptDir\Product.wxs"
if ($LASTEXITCODE -ne 0) { throw "candle failed for Product.wxs" }

Write-Host "Linking wixobj to MSI with light"
& $light -out "$scriptDir\DAISY-Braille-Toolkit.msi" "$scriptDir\HarvestedComponents.wixobj" "$scriptDir\Product.wixobj" -sval
if ($LASTEXITCODE -ne 0) { throw "light failed" }

Write-Host "MSI created at $scriptDir\DAISY-Braille-Toolkit.msi"

# Copy published single-file exe to Installer folder for direct distribution
try {
    $exeName = Get-ChildItem -Path $publishDir -Filter "*.exe" | Where-Object { $_.Name -ne "dotnet.exe" } | Select-Object -First 1
    if ($exeName) {
        Copy-Item -Path $exeName.FullName -Destination (Join-Path $scriptDir $exeName.Name) -Force
        Write-Host "Copied standalone EXE to $scriptDir\$($exeName.Name)"
    } else {
        Write-Host "No standalone exe found in publish dir"
    }
} catch {
    Write-Host "Failed to copy standalone EXE: $_"
}

# Optional signing: provide SIGNING_PFX (path) and SIGNING_PFX_PASS (password) as environment variables
if ($env:SIGNING_PFX -and $env:SIGNING_PFX_PASS) {
    $msiPath = Join-Path $scriptDir "DAISY-Braille-Toolkit.msi"
    $signtool = "signtool.exe"
    Write-Host "Signing MSI using signtool and PFX from SIGNING_PFX"
    & $signtool sign /f "$env:SIGNING_PFX" /p "$env:SIGNING_PFX_PASS" /tr http://timestamp.digicert.com /td sha256 /fd sha256 "$msiPath"
    if ($LASTEXITCODE -ne 0) { throw "signtool failed to sign MSI" }
    Write-Host "MSI signed"
} else {
    Write-Host "SIGNING_PFX or SIGNING_PFX_PASS not provided — skipping signing"
}
