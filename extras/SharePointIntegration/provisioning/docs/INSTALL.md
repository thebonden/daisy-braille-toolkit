# DBT SharePoint Installation (Open Source)

This package provisions the SharePoint Lists required by **DAISY-Braille Toolkit**.

## What it does
- Creates these lists (if missing):
  - DBT_Counters
  - DBT_Productions
  - DBT_Lookup_EmployeeAbbrev
  - DBT_Lookup_ProducedFor
  - DBT_Lookup_ProducedFrom
  - DBT_Lookup_ReturnAddress
- Adds required columns + unique constraints (e.g. DateKey, VolumeLabel, Abbrev)
- **Does not** attempt to delete the SharePoint **Title** column (impossible).  
  On address lists, Title is renamed to `InternalTitle` and kept optional, so it can remain empty.

## Prerequisites
- Windows PowerShell 7+ (pwsh) recommended
- Install PnP.PowerShell:
  ```powershell
  Install-Module PnP.PowerShell -Scope CurrentUser
  ```

## Run (interactive sign-in)
```powershell
cd .\scripts
pwsh .\Install-DBTSharePoint.ps1 -SiteUrl "https://<tenant>.sharepoint.com/sites/<SiteName>"
```

## Optional: import seed data
```powershell
cd .\scripts
pwsh .\Install-DBTSharePoint.ps1 -SiteUrl "https://<tenant>.sharepoint.com/sites/<SiteName>" -ImportSeedData
```

Seed CSV files live in `seed-data/`.

## Notes
- SharePoint “Export to CSV” exports **the current view**. Create a dedicated view for Epson exports if needed.

Generated: 2026-01-25T11:39:05
