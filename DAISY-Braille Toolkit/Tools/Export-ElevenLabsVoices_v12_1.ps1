<#
.SYNOPSIS
  Export ElevenLabs voices (MY voices + optional Voice Library) to CSV/XLSX/JSON and write two JSON files:
    - voices.json           = MY voices only (selectable in your account)
    - voices_library.json   = Voice Library (shared) voices

WHAT'S NEW (v12.1)
  - Fixes a syntax bug in v12 where voice_id line could break the object literal.

Adds fields:
  - in_my_voices (true/false)
  - likely_selectable_now (true/false) [true only if in_my_voices]

.SECRETS JSON
  Must contain ELEVEN_API_KEY.
#>

[CmdletBinding()]
param(
  [string]$SecretsPath = ".\secrets.json",
  [string]$OutDir = ".\out",
  [switch]$CsvOnly,
  [switch]$PauseOnExit,

  # Write json files in script folder
  [switch]$RebuildVoicesJson,
  [string]$MyVoicesJsonPath = "voices.json",
  [string]$LibraryVoicesJsonPath = "voices_library.json",

  # voice library
  [switch]$IncludeVoiceLibrary,
  [int]$SharedPageSize = 100,          # 1-100
  [int]$SharedMaxVoices = 50000,       # default higher for "complete" requests
  [string]$SharedSearch = ""           # optional search term to limit shared voices
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Pause-IfRequested {
  param([switch]$DoPause)
  if ($DoPause) {
    Write-Host ""
    Write-Host "Press ENTER to close..." -ForegroundColor Yellow
    [void](Read-Host)
  }
}

function Get-ProviderPath {
  param([Parameter(Mandatory=$true)][string]$Path)
  return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

$ScriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ScriptDir)) { $ScriptDir = Split-Path -Parent $PSCommandPath }

function Resolve-SecretsPath {
  param([string]$Path)

  $candidates = @()
  if ($Path) { $candidates += $Path }

  $candidates += @(
    ".\secrets1.json",
    (Join-Path -Path $ScriptDir -ChildPath "secrets.json"),
    (Join-Path -Path $ScriptDir -ChildPath "secrets1.json")
  ) | Select-Object -Unique

  foreach ($c in $candidates) {
    if (Test-Path -LiteralPath $c) { return (Get-ProviderPath -Path $c) }
  }

  throw "Secrets file not found. Tried: $($candidates -join ', ')"
}

function Resolve-PathRelativeToScript {
  param([string]$Path, [string]$DefaultName)
  if ([string]::IsNullOrWhiteSpace($Path)) { $Path = $DefaultName }
  if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
  return (Join-Path -Path $ScriptDir -ChildPath $Path)
}

function Get-PSProp {
  param(
    [Parameter(Mandatory=$true)]$Obj,
    [Parameter(Mandatory=$true)][string[]]$Names
  )
  if ($null -eq $Obj) { return $null }
  foreach ($n in $Names) {
    $p = $Obj.PSObject.Properties[$n]
    if ($null -ne $p) { return $p.Value }
  }
  return $null
}

function Get-LabelValue {
  param(
    [object]$Labels,
    [string[]]$Keys
  )
  if ($null -eq $Labels) { return "" }

  $dict = @{}
  foreach ($p in $Labels.PSObject.Properties) {
    $dict[$p.Name.ToLowerInvariant()] = $p.Value
  }

  foreach ($k in $Keys) {
    $lk = $k.ToLowerInvariant()
    if ($dict.ContainsKey($lk) -and $null -ne $dict[$lk]) {
      $v = $dict[$lk]
      if ($v -is [System.Collections.IEnumerable] -and -not ($v -is [string])) {
        return (($v | ForEach-Object { "$_" }) -join ",")
      }
      return "$v"
    }
  }
  return ""
}

function Looks-DaOrEn {
  param([pscustomobject]$Row)

  $hay = (@(
    $Row.name,
    $Row.description,
    $Row.accent,
    $Row.languages,
    $Row.category
  ) | ForEach-Object { "$_" }) -join " | "
  $hay = $hay.ToLowerInvariant()

  $da = $hay -match '\bdanish\b|\bdansk\b|\bda\b|\bdenmark\b|\bdk\b'
  $en = $hay -match '\benglish\b|\ben\b|\bbritish\b|\buk\b|\bamerican\b|\bus\b'
  return ($da -or $en)
}

function Get-SharedLanguages {
  param([object]$SharedVoice)
  $vl = Get-PSProp -Obj $SharedVoice -Names @("verified_languages","languages","verifiedLanguages")
  if ($null -eq $vl) { return "" }
  $parts = @()
  foreach ($l in $vl) {
    $lang = Get-PSProp -Obj $l -Names @("language","lang")
    $acc  = Get-PSProp -Obj $l -Names @("accent")
    if ($lang) {
      if ($acc) { $parts += ("{0} ({1})" -f $lang, $acc) }
      else { $parts += ("{0}" -f $lang) }
    }
  }
  return ($parts -join ", ")
}

function Fetch-SharedVoices {
  param(
    [Parameter(Mandatory=$true)][hashtable]$Headers,
    [Parameter(Mandatory=$true)][int]$PageSize,
    [Parameter(Mandatory=$true)][int]$MaxVoices,
    [string]$Search = ""
  )
  if ($PageSize -lt 1 -or $PageSize -gt 100) { throw "SharedPageSize must be between 1 and 100." }

  $page = 0
  $all = @()
  while ($all.Count -lt $MaxVoices) {
    $url = "https://api.elevenlabs.io/v1/shared-voices?page=$page&page_size=$PageSize"
    if (-not [string]::IsNullOrWhiteSpace($Search)) {
      $url += "&search=" + [uri]::EscapeDataString($Search)
    }

    Write-Host ("Fetching shared voices page {0} ..." -f $page)
    $resp = Invoke-RestMethod -Method Get -Uri $url -Headers $Headers -TimeoutSec 60

    $voices = Get-PSProp -Obj $resp -Names @("voices","shared_voices","items")
    if ($null -eq $voices -or $voices.Count -eq 0) { break }

    foreach ($sv in $voices) {
      $all += $sv
      if ($all.Count -ge $MaxVoices) { break }
    }
    $page++
  }

  return ,$all
}

function Build-MyRows {
  param(
    [Parameter(Mandatory=$true)]$ApiResponse,
    [Parameter(Mandatory=$true)][string]$FetchedOn,
    [Parameter(Mandatory=$true)][string]$SourceUrl
  )

  $rows = foreach ($v in ($ApiResponse.voices | ForEach-Object { $_ })) {
    $labels = $v.labels

    $fineTuning = Get-PSProp -Obj $v -Names @("fine_tuning","fineTuning","fine_tuning_settings")
    $ftState = Get-PSProp -Obj $fineTuning -Names @("finetuning_state","fine_tuning_state","fineTuningState","state")
    $ftAllowed = Get-PSProp -Obj $fineTuning -Names @("is_allowed_to_fine_tune","isAllowedToFineTune","allowed","can_fine_tune")

    [pscustomobject]@{
      fetched_on   = $FetchedOn
      source_url   = $SourceUrl
      source_type  = "my"
      name         = $v.name
      voice_id     = $v.voice_id
      gender       = Get-LabelValue -Labels $labels -Keys @("gender","sex")
      age          = Get-LabelValue -Labels $labels -Keys @("age")
      accent       = Get-LabelValue -Labels $labels -Keys @("accent")
      description  = $v.description
      use_case     = Get-LabelValue -Labels $labels -Keys @("use_case","usecase","use-case")
      preview_url  = $v.preview_url
      languages    = Get-LabelValue -Labels $labels -Keys @("languages","language","lang","locale")

      category     = $v.category
      available_for_tiers     = (($v.available_for_tiers | ForEach-Object { "$_" }) -join ",")
      fine_tuning_state       = $ftState
      is_allowed_to_fine_tune = $ftAllowed
      labels_raw              = ($labels | ConvertTo-Json -Compress -Depth 6)

      in_my_voices            = $true
      likely_selectable_now   = $true
    }
  }
  return ,$rows
}

function Build-SharedRows {
  param(
    [Parameter(Mandatory=$true)][object[]]$SharedVoices,
    [Parameter(Mandatory=$true)][string]$FetchedOn,
    [Parameter(Mandatory=$true)][hashtable]$MyIdSet
  )

  $rows = foreach ($sv in $SharedVoices) {
    $id = Get-PSProp -Obj $sv -Names @("voice_id","voiceId","id")
    $inMine = $false
    if ($id -and $MyIdSet.ContainsKey($id)) { $inMine = $true }

    [pscustomobject]@{
      fetched_on   = $FetchedOn
      source_url   = "https://api.elevenlabs.io/v1/shared-voices"
      source_type  = "shared"
      name         = Get-PSProp -Obj $sv -Names @("name")
      voice_id     = $id
      gender       = Get-PSProp -Obj $sv -Names @("gender")
      age          = Get-PSProp -Obj $sv -Names @("age")
      accent       = Get-PSProp -Obj $sv -Names @("accent")
      description  = Get-PSProp -Obj $sv -Names @("description")
      use_case     = Get-PSProp -Obj $sv -Names @("use_case","useCase")
      preview_url  = Get-PSProp -Obj $sv -Names @("preview_url","previewUrl")
      languages    = (Get-SharedLanguages -SharedVoice $sv)

      category     = Get-PSProp -Obj $sv -Names @("category")
      available_for_tiers     = ""
      fine_tuning_state       = $null
      is_allowed_to_fine_tune = $null
      labels_raw              = ($sv | ConvertTo-Json -Compress -Depth 8)

      in_my_voices            = $inMine
      likely_selectable_now   = $inMine
    }
  }
  return ,$rows
}

function Write-JsonUtf8 {
  param([Parameter(Mandatory=$true)][object]$Obj, [Parameter(Mandatory=$true)][string]$Path, [int]$Depth = 12)
  ($Obj | ConvertTo-Json -Depth $Depth) | Set-Content -Path $Path -Encoding UTF8
}

try {
  New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
  $outDirFull = Get-ProviderPath -Path $OutDir

  $logPath = Join-Path $outDirFull ("elevenlabs_export_log_{0}.txt" -f (Get-Date -Format "yyyyMMdd_HHmmss"))
  Start-Transcript -Path $logPath -Force | Out-Null

  Write-Host "Script dir: $ScriptDir"
  Write-Host "Output folder: $outDirFull"
  Write-Host "Log file: $logPath"

  $SecretsPath = Resolve-SecretsPath -Path $SecretsPath
  Write-Host "Secrets file: $SecretsPath"

  $secretsRaw = Get-Content -LiteralPath $SecretsPath -Raw
  if ([string]::IsNullOrWhiteSpace($secretsRaw)) { throw "Secrets file is empty: $SecretsPath" }
  $secrets = $secretsRaw | ConvertFrom-Json

  $apiKey = $null
  if ($secrets.PSObject.Properties.Name -contains "ELEVEN_API_KEY") { $apiKey = $secrets.ELEVEN_API_KEY }
  if ([string]::IsNullOrWhiteSpace($apiKey) -and ($secrets.PSObject.Properties.Name -contains "ELEVENLABS_API_KEY")) { $apiKey = $secrets.ELEVENLABS_API_KEY }
  if ([string]::IsNullOrWhiteSpace($apiKey) -and ($secrets.PSObject.Properties.Name -contains "XI_API_KEY")) { $apiKey = $secrets.XI_API_KEY }
  if ([string]::IsNullOrWhiteSpace($apiKey)) { throw "API key is empty. Please add ELEVEN_API_KEY to $SecretsPath" }
  $apiKey = $apiKey.Trim()

  $headers = @{ "xi-api-key" = $apiKey; "accept" = "application/json" }
  $now = (Get-Date).ToUniversalTime().ToString("o")

  # MY VOICES
  $myUrl = "https://api.elevenlabs.io/v1/voices"
  Write-Host "Fetching MY voices from $myUrl ..."
  $myResp = Invoke-RestMethod -Method Get -Uri $myUrl -Headers $headers -TimeoutSec 60
  if ($null -eq $myResp -or $null -eq $myResp.voices) { throw "Unexpected API response from /v1/voices (no 'voices' array)." }

  $myRows = Build-MyRows -ApiResponse $myResp -FetchedOn $now -SourceUrl $myUrl
  $mySorted = $myRows | Sort-Object languages, accent, name
  $myDaEn = $mySorted | Where-Object { Looks-DaOrEn $_ }

  $myIdSet = @{}
  foreach ($r in $mySorted) { if ($r.voice_id) { $myIdSet[$r.voice_id] = $true } }

  # SHARED (optional)
  $sharedSorted = @()
  $sharedDaEn = @()

  if ($IncludeVoiceLibrary) {
    Write-Host ("Fetching Voice Library (shared voices) up to {0} voices..." -f $SharedMaxVoices)
    $sharedVoices = Fetch-SharedVoices -Headers $headers -PageSize $SharedPageSize -MaxVoices $SharedMaxVoices -Search $SharedSearch
    $sharedRows = Build-SharedRows -SharedVoices $sharedVoices -FetchedOn $now -MyIdSet $myIdSet
    $sharedSorted = $sharedRows | Sort-Object languages, accent, name
    $sharedDaEn = $sharedSorted | Where-Object { Looks-DaOrEn $_ }
  }

  # Write exports (MY)
  $myCsvAll  = Join-Path $outDirFull "elevenlabs_my_voices_all.csv"
  $myCsvDaEn = Join-Path $outDirFull "elevenlabs_my_voices_da_en.csv"
  $myJsonAll  = Join-Path $outDirFull "elevenlabs_my_voices_all.json"
  $myJsonDaEn = Join-Path $outDirFull "elevenlabs_my_voices_da_en.json"

  $mySorted | Export-Csv -Path $myCsvAll -NoTypeInformation -Encoding UTF8
  $myDaEn   | Export-Csv -Path $myCsvDaEn -NoTypeInformation -Encoding UTF8
  Write-JsonUtf8 -Obj $mySorted -Path $myJsonAll -Depth 12
  Write-JsonUtf8 -Obj $myDaEn   -Path $myJsonDaEn -Depth 12

  Write-Host "Wrote MY voices:"
  Write-Host " - $myCsvAll"
  Write-Host " - $myJsonAll"
  Write-Host ("My voices: {0} | DA/EN match: {1}" -f $mySorted.Count, $myDaEn.Count)

  # Write exports (Library)
  if ($IncludeVoiceLibrary -and $sharedSorted.Count -gt 0) {
    $shCsvAll  = Join-Path $outDirFull "elevenlabs_voice_library_all.csv"
    $shCsvDaEn = Join-Path $outDirFull "elevenlabs_voice_library_da_en.csv"
    $shJsonAll  = Join-Path $outDirFull "elevenlabs_voice_library_all.json"
    $shJsonDaEn = Join-Path $outDirFull "elevenlabs_voice_library_da_en.json"

    $sharedSorted | Export-Csv -Path $shCsvAll -NoTypeInformation -Encoding UTF8
    $sharedDaEn   | Export-Csv -Path $shCsvDaEn -NoTypeInformation -Encoding UTF8
    Write-JsonUtf8 -Obj $sharedSorted -Path $shJsonAll -Depth 12
    Write-JsonUtf8 -Obj $sharedDaEn   -Path $shJsonDaEn -Depth 12

    Write-Host "Wrote VOICE LIBRARY:"
    Write-Host " - $shCsvAll"
    Write-Host " - $shJsonAll"
    Write-Host ("Voice Library fetched: {0} | DA/EN match: {1}" -f $sharedSorted.Count, $sharedDaEn.Count)
  }

  # XLSX
  if (-not $CsvOnly) {
    if (Get-Module -ListAvailable -Name ImportExcel) {
      Import-Module ImportExcel | Out-Null
      $xlsx = Join-Path $outDirFull "elevenlabs_voices.xlsx"
      if (Test-Path $xlsx) { Remove-Item $xlsx -Force }

      $mySorted | Export-Excel -Path $xlsx -WorksheetName "my_all" -AutoSize -FreezeTopRow -TableName "MyAll"
      $myDaEn   | Export-Excel -Path $xlsx -WorksheetName "my_da_en" -AutoSize -FreezeTopRow -TableName "MyDaEn" -Append

      if ($IncludeVoiceLibrary -and $sharedSorted.Count -gt 0) {
        $sharedSorted | Export-Excel -Path $xlsx -WorksheetName "library_all" -AutoSize -FreezeTopRow -TableName "LibAll" -Append
        $sharedDaEn   | Export-Excel -Path $xlsx -WorksheetName "library_da_en" -AutoSize -FreezeTopRow -TableName "LibDaEn" -Append
      }

      Write-Host "Wrote XLSX:"
      Write-Host " - $xlsx"
    } else {
      Write-Warning "ImportExcel module not found, so XLSX was not created. CSV/JSON files were created instead."
      Write-Host "To enable XLSX export, run (once):"
      Write-Host "  Install-Module ImportExcel -Scope CurrentUser -Force"
    }
  }

  # Write two json files in script folder
  if ($RebuildVoicesJson) {
    $myJsonPathFull = Resolve-PathRelativeToScript -Path $MyVoicesJsonPath -DefaultName "voices.json"
    $libJsonPathFull = Resolve-PathRelativeToScript -Path $LibraryVoicesJsonPath -DefaultName "voices_library.json"

    foreach ($p in @($myJsonPathFull, $libJsonPathFull)) {
      $d = Split-Path -Parent $p
      if (-not [string]::IsNullOrWhiteSpace($d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
    }

    Write-JsonUtf8 -Obj ($mySorted | Sort-Object name) -Path $myJsonPathFull -Depth 12
    Write-Host ("voices.json written (MY selectable). Total: {0}" -f $mySorted.Count)
    Write-Host " - $myJsonPathFull"

    if ($IncludeVoiceLibrary -and $sharedSorted.Count -gt 0) {
      Write-JsonUtf8 -Obj ($sharedSorted | Sort-Object name) -Path $libJsonPathFull -Depth 12
      Write-Host ("voices_library.json written (Library catalog). Total: {0}" -f $sharedSorted.Count)
      Write-Host " - $libJsonPathFull"
    } else {
      Write-Host "Note: voices_library.json not written because -IncludeVoiceLibrary was not used (or no library voices fetched)."
    }
  }

  Stop-Transcript | Out-Null
  Pause-IfRequested -DoPause:$PauseOnExit
}
catch {
  try { Stop-Transcript | Out-Null } catch {}
  Write-Host ""
  Write-Host ("ERROR: {0}" -f $_.Exception.Message) -ForegroundColor Red
  if ($_.Exception.InnerException) {
    Write-Host ("INNER: {0}" -f $_.Exception.InnerException.Message) -ForegroundColor DarkRed
  }
  Write-Host ""
  Write-Host "Tip: Check the log file in OutDir for details." -ForegroundColor Yellow
  Pause-IfRequested -DoPause:$true
  exit 1
}
