<#
DBT (DAISY-Braille Toolkit) - SharePoint installer/provisioner (Open Source)

Goal:
- Create all required SharePoint Lists with correct columns and constraints
- DO NOT try to delete the SharePoint "Title" column (it cannot be deleted)
- Keep Title as an "InternalTitle" field on address lists; it can remain empty
- Optionally import seed CSV data

Prereqs:
  Install-Module PnP.PowerShell -Scope CurrentUser

Usage:
  pwsh .\Install-DBTSharePoint.ps1 -SiteUrl "https://<tenant>.sharepoint.com/sites/<SiteName>"

Optional seed import:
  pwsh .\Install-DBTSharePoint.ps1 -SiteUrl "https://<tenant>.sharepoint.com/sites/<SiteName>" -ImportSeedData

Seed folder:
  .\seed-data (CSV headers should match field internal names)

#>

[CmdletBinding(SupportsShouldProcess=$true)]
param(
  [Parameter(Mandatory=$true)]
  [string]$SiteUrl,

  [switch]$ImportSeedData,

  [string]$SeedDataPath = (Join-Path $PSScriptRoot "..\seed-data")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Info($m){ Write-Host "[DBT] $m" -ForegroundColor Cyan }
function Warn($m){ Write-Host "[DBT] WARNING: $m" -ForegroundColor Yellow }

function Ensure-PnP {
  if (-not (Get-Module -ListAvailable -Name PnP.PowerShell)) {
    throw "PnP.PowerShell is not installed. Run: Install-Module PnP.PowerShell -Scope CurrentUser"
  }
}

function Connect-Dbt {
  Info "Connecting (interactive)..."
  Connect-PnPOnline -Url $SiteUrl -Interactive
}

function Ensure-List([string]$Title, [string]$Description="") {
  $l = Get-PnPList -Identity $Title -ErrorAction SilentlyContinue
  if ($null -ne $l) { Info "List exists: $Title"; return }
  if ($PSCmdlet.ShouldProcess($Title, "Create list")) {
    Info "Creating list: $Title"
    Add-PnPList -Title $Title -Template GenericList -OnQuickLaunch:$false -EnableContentTypes:$true -Description $Description | Out-Null
  }
}

function Ensure-TextField {
  param(
    [Parameter(Mandatory=$true)][string]$List,
    [Parameter(Mandatory=$true)][string]$InternalName,
    [Parameter(Mandatory=$true)][string]$DisplayName,
    [int]$MaxLength=255,
    [bool]$Required=$false,
    [bool]$Unique=$false,
    [bool]$AddToDefaultView=$true
  )
  $f = Get-PnPField -List $List -Identity $InternalName -ErrorAction SilentlyContinue
  if ($null -eq $f) {
    if ($PSCmdlet.ShouldProcess("$List/$InternalName", "Create text field")) {
      Add-PnPField -List $List -Type Text -InternalName $InternalName -DisplayName $DisplayName -AddToDefaultView:$AddToDefaultView | Out-Null
    }
  }
  $vals=@{ Title=$DisplayName; MaxLength=$MaxLength; Required=$Required }
  if ($Unique) { $vals["EnforceUniqueValues"]=$true; $vals["Indexed"]=$true } else { $vals["EnforceUniqueValues"]=$false }
  if ($PSCmdlet.ShouldProcess("$List/$InternalName", "Update field")) {
    Set-PnPField -List $List -Identity $InternalName -Values $vals | Out-Null
  }
}

function Ensure-NumberField {
  param(
    [Parameter(Mandatory=$true)][string]$List,
    [Parameter(Mandatory=$true)][string]$InternalName,
    [Parameter(Mandatory=$true)][string]$DisplayName,
    [bool]$Required=$false,
    [bool]$Unique=$false,
    [int]$Decimals=0,
    [bool]$AddToDefaultView=$true
  )
  $f = Get-PnPField -List $List -Identity $InternalName -ErrorAction SilentlyContinue
  if ($null -eq $f) {
    if ($PSCmdlet.ShouldProcess("$List/$InternalName", "Create number field")) {
      Add-PnPField -List $List -Type Number -InternalName $InternalName -DisplayName $DisplayName -AddToDefaultView:$AddToDefaultView | Out-Null
    }
  }
  $vals=@{ Title=$DisplayName; Required=$Required; Decimals=$Decimals }
  if ($Unique) { $vals["EnforceUniqueValues"]=$true; $vals["Indexed"]=$true } else { $vals["EnforceUniqueValues"]=$false }
  if ($PSCmdlet.ShouldProcess("$List/$InternalName", "Update field")) {
    Set-PnPField -List $List -Identity $InternalName -Values $vals | Out-Null
  }
}

function Ensure-DateTimeField {
  param(
    [Parameter(Mandatory=$true)][string]$List,
    [Parameter(Mandatory=$true)][string]$InternalName,
    [Parameter(Mandatory=$true)][string]$DisplayName,
    [bool]$Required=$false,
    [bool]$AddToDefaultView=$true
  )
  $f = Get-PnPField -List $List -Identity $InternalName -ErrorAction SilentlyContinue
  if ($null -eq $f) {
    if ($PSCmdlet.ShouldProcess("$List/$InternalName", "Create datetime field")) {
      Add-PnPField -List $List -Type DateTime -InternalName $InternalName -DisplayName $DisplayName -AddToDefaultView:$AddToDefaultView | Out-Null
    }
  }
  if ($PSCmdlet.ShouldProcess("$List/$InternalName", "Update field")) {
    Set-PnPField -List $List -Identity $InternalName -Values @{ Title=$DisplayName; Required=$Required } | Out-Null
  }
}

function Ensure-UserField {
  param(
    [Parameter(Mandatory=$true)][string]$List,
    [Parameter(Mandatory=$true)][string]$InternalName,
    [Parameter(Mandatory=$true)][string]$DisplayName,
    [bool]$Required=$false,
    [bool]$AddToDefaultView=$true
  )
  $f = Get-PnPField -List $List -Identity $InternalName -ErrorAction SilentlyContinue
  if ($null -eq $f) {
    if ($PSCmdlet.ShouldProcess("$List/$InternalName", "Create person field")) {
      Add-PnPField -List $List -Type User -InternalName $InternalName -DisplayName $DisplayName -AddToDefaultView:$AddToDefaultView | Out-Null
    }
  }
  if ($PSCmdlet.ShouldProcess("$List/$InternalName", "Update field")) {
    Set-PnPField -List $List -Identity $InternalName -Values @{ Title=$DisplayName; Required=$Required } | Out-Null
  }
}

function Ensure-ChoiceField {
  param(
    [Parameter(Mandatory=$true)][string]$List,
    [Parameter(Mandatory=$true)][string]$InternalName,
    [Parameter(Mandatory=$true)][string]$DisplayName,
    [Parameter(Mandatory=$true)][string[]]$Choices,
    [string]$DefaultValue="",
    [bool]$Required=$false,
    [bool]$AddToDefaultView=$true
  )
  $f = Get-PnPField -List $List -Identity $InternalName -ErrorAction SilentlyContinue
  if ($null -eq $f) {
    if ($PSCmdlet.ShouldProcess("$List/$InternalName", "Create choice field")) {
      Add-PnPField -List $List -Type Choice -InternalName $InternalName -DisplayName $DisplayName -Choices $Choices -AddToDefaultView:$AddToDefaultView | Out-Null
    }
  }
  if ($PSCmdlet.ShouldProcess("$List/$InternalName", "Update field")) {
    Set-PnPField -List $List -Identity $InternalName -Values @{ Title=$DisplayName; Choices=$Choices; Required=$Required } | Out-Null
  }
  if ($DefaultValue) {
    if ($PSCmdlet.ShouldProcess("$List/$InternalName", "Set default")) {
      Set-PnPField -List $List -Identity $InternalName -Values @{ DefaultValue=$DefaultValue } | Out-Null
    }
  }
}

function Try-SetValidation {
  param(
    [Parameter(Mandatory=$true)][string]$List,
    [Parameter(Mandatory=$true)][string]$InternalName,
    [Parameter(Mandatory=$true)][string]$FormulaComma,
    [Parameter(Mandatory=$true)][string]$FormulaSemicolon,
    [Parameter(Mandatory=$true)][string]$Message
  )
  try {
    Set-PnPField -List $List -Identity $InternalName -Values @{ ValidationFormula=$FormulaComma; ValidationMessage=$Message } | Out-Null
    return
  } catch {
    try {
      Set-PnPField -List $List -Identity $InternalName -Values @{ ValidationFormula=$FormulaSemicolon; ValidationMessage=$Message } | Out-Null
      return
    } catch {
      Warn "Could not set validation for $List/$InternalName automatically. Set it manually if you want. ($($_.Exception.Message))"
    }
  }
}

function Configure-TitleAsInternal {
  param(
    [Parameter(Mandatory=$true)][string]$List,
    [Parameter(Mandatory=$true)][string]$DisplayName
  )
  # Title column cannot be deleted. We make it optional and not unique, and rename display name.
  if ($PSCmdlet.ShouldProcess("$List/Title", "Configure Title column")) {
    Set-PnPField -List $List -Identity "Title" -Values @{
      Title = $DisplayName
      Required = $false
      EnforceUniqueValues = $false
      Indexed = $false
    } | Out-Null
  }
}

function Configure-TitleAsRequiredUnique {
  param(
    [Parameter(Mandatory=$true)][string]$List,
    [Parameter(Mandatory=$true)][string]$DisplayName
  )
  if ($PSCmdlet.ShouldProcess("$List/Title", "Configure Title column required+unique")) {
    Set-PnPField -List $List -Identity "Title" -Values @{
      Title = $DisplayName
      Required = $true
      EnforceUniqueValues = $true
      Indexed = $true
    } | Out-Null
  }
}

function Import-SeedCsvToList {
  param(
    [Parameter(Mandatory=$true)][string]$CsvFile,
    [Parameter(Mandatory=$true)][string]$ListTitle
  )
  Info "Importing seed CSV '$CsvFile' -> '$ListTitle'"
  $rows = Import-Csv -Path $CsvFile
  if (-not $rows -or $rows.Count -eq 0) { Info "  (no rows)"; return }
  foreach ($r in $rows) {
    $values=@{}
    foreach ($p in $r.PSObject.Properties) {
      if ($null -ne $p.Value -and ($p.Value.ToString().Trim().Length -gt 0)) {
        $values[$p.Name] = $p.Value
      }
    }
    if ($values.Count -eq 0) { continue }
    try {
      Add-PnPListItem -List $ListTitle -Values $values | Out-Null
    } catch {
      Warn "  Skipped row (duplicates/required): $($_.Exception.Message)"
    }
  }
}

# ------------------------
# MAIN
# ------------------------
Ensure-PnP
Connect-Dbt

# DBT_Counters
Ensure-List "DBT_Counters" "Internal counters used to reserve unique production IDs."
Ensure-TextField   -List "DBT_Counters" -InternalName "DateKey" -DisplayName "DateKey" -MaxLength 8 -Required $true -Unique $true
Ensure-TextField   -List "DBT_Counters" -InternalName "Prefix" -DisplayName "Prefix" -MaxLength 10 -Required $true
Ensure-NumberField -List "DBT_Counters" -InternalName "NextNumber" -DisplayName "NextNumber" -Required $true -Decimals 0

# DBT_Productions (catalog/log)
Ensure-List "DBT_Productions" "Production log (metadata catalog)."
Ensure-TextField   -List "DBT_Productions" -InternalName "VolumeLabel" -DisplayName "VolumeLabel" -MaxLength 255 -Required $true -Unique $true
Ensure-TextField   -List "DBT_Productions" -InternalName "Prefix" -DisplayName "Prefix" -MaxLength 10 -Required $true
Ensure-TextField   -List "DBT_Productions" -InternalName "DateKey" -DisplayName "DateKey" -MaxLength 8 -Required $true
Ensure-NumberField -List "DBT_Productions" -InternalName "Sequence" -DisplayName "Sequence" -Required $true -Decimals 0

Ensure-ChoiceField -List "DBT_Productions" -InternalName "Status" -DisplayName "Status" -Choices @("Reserved","InProgress","Completed","Cancelled") -DefaultValue "Reserved"

Ensure-DateTimeField -List "DBT_Productions" -InternalName "ReservedAt" -DisplayName "ReservedAt"
Ensure-UserField     -List "DBT_Productions" -InternalName "ReservedBy" -DisplayName "ReservedBy"

# DBT_Lookup_EmployeeAbbrev (Title used as Abbrev)
Ensure-List "DBT_Lookup_EmployeeAbbrev" "Employee abbreviations used in metadata."
Configure-TitleAsRequiredUnique -List "DBT_Lookup_EmployeeAbbrev" -DisplayName "Abbrev"
Ensure-TextField -List "DBT_Lookup_EmployeeAbbrev" -InternalName "FullName" -DisplayName "FullName" -MaxLength 255 -Required $false

Try-SetValidation -List "DBT_Lookup_EmployeeAbbrev" -InternalName "Title" `
  -FormulaComma '=AND(LEN([Abbrev])=3,[Abbrev]=UPPER([Abbrev]),ISERROR(FIND(" ",[Abbrev])))' `
  -FormulaSemicolon '=AND(LEN([Abbrev])=3;[Abbrev]=UPPER([Abbrev]);ISERROR(FIND(" ";[Abbrev])))' `
  -Message 'Abbrev must be exactly 3 uppercase characters (no spaces).'

# Address base columns (helper)
function Ensure-AddressColumns([string]$List, [bool]$IncludeCode) {
  Configure-TitleAsInternal -List $List -DisplayName "InternalTitle"   # OPTIONAL, can remain empty
  if ($IncludeCode) {
    Ensure-TextField -List $List -InternalName "Code" -DisplayName "Code" -MaxLength 5 -Required $true -Unique $true
    Try-SetValidation -List $List -InternalName "Code" `
      -FormulaComma '=AND(LEN([Code])>=3,LEN([Code])<=5,[Code]=UPPER([Code]),ISERROR(FIND(" ",[Code])))' `
      -FormulaSemicolon '=AND(LEN([Code])>=3;LEN([Code])<=5;[Code]=UPPER([Code]);ISERROR(FIND(" ";[Code])))' `
      -Message 'Code must be 3–5 uppercase characters (no spaces). Recommended: A–Z and 0–9 only.'
  }

  Ensure-TextField -List $List -InternalName "OrganizationName" -DisplayName "OrganizationName" -MaxLength 255 -Required $true
  Ensure-TextField -List $List -InternalName "Department" -DisplayName "Department" -MaxLength 255
  Ensure-TextField -List $List -InternalName "Attention" -DisplayName "Attention" -MaxLength 255

  Ensure-TextField -List $List -InternalName "AddressLine1" -DisplayName "AddressLine1" -MaxLength 255 -Required $true
  Ensure-TextField -List $List -InternalName "AddressLine2" -DisplayName "AddressLine2" -MaxLength 255
  Ensure-TextField -List $List -InternalName "PostalCode" -DisplayName "PostalCode" -MaxLength 32 -Required $true
  Ensure-TextField -List $List -InternalName "City" -DisplayName "City" -MaxLength 128 -Required $true
  Ensure-TextField -List $List -InternalName "StateOrRegion" -DisplayName "StateOrRegion" -MaxLength 128
  Ensure-TextField -List $List -InternalName "Country" -DisplayName "Country" -MaxLength 128 -Required $true

  Ensure-TextField -List $List -InternalName "Email" -DisplayName "Email" -MaxLength 255
  Ensure-TextField -List $List -InternalName "Phone" -DisplayName "Phone" -MaxLength 64
  Ensure-TextField -List $List -InternalName "Notes" -DisplayName "Notes" -MaxLength 255
}

# DBT_Lookup_ProducedFor (includes Code)
Ensure-List "DBT_Lookup_ProducedFor" "Organizations you produce for."
Ensure-AddressColumns -List "DBT_Lookup_ProducedFor" -IncludeCode $true

# DBT_Lookup_ProducedFrom (same, no Code)
Ensure-List "DBT_Lookup_ProducedFrom" "Where the request originated."
Ensure-AddressColumns -List "DBT_Lookup_ProducedFrom" -IncludeCode $false

# DBT_Lookup_ReturnAddress (same, no Code)
Ensure-List "DBT_Lookup_ReturnAddress" "Return address presets."
Ensure-AddressColumns -List "DBT_Lookup_ReturnAddress" -IncludeCode $false

Info "Provisioning completed."

if ($ImportSeedData) {
  Info "ImportSeedData enabled."
  if (-not (Test-Path $SeedDataPath)) { throw "SeedDataPath not found: $SeedDataPath" }

  $map = @{
    "DBT_Lookup_EmployeeAbbrev.csv" = "DBT_Lookup_EmployeeAbbrev"
    "DBT_Lookup_ProducedFor.csv"   = "DBT_Lookup_ProducedFor"
    "DBT_Lookup_ProducedFrom.csv"  = "DBT_Lookup_ProducedFrom"
    "DBT_Lookup_ReturnAddress.csv" = "DBT_Lookup_ReturnAddress"
  }

  foreach ($k in $map.Keys) {
    $csv = Join-Path $SeedDataPath $k
    if (Test-Path $csv) { Import-SeedCsvToList -CsvFile $csv -ListTitle $map[$k] }
    else { Warn "Seed CSV not found: $csv" }
  }
  Info "Seed import completed."
}

Info "Done."