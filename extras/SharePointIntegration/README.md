# DAISY-Braille Toolkit – SharePoint Integration (Open Source)

This repository contains:
- **Program (sample app)** under `/src/DbtSharePointSample` demonstrating:
  - MSAL delegated sign-in (no secrets)
  - Microsoft Graph access to SharePoint Lists
  - Robust counter reservation using ETag retry (`DBT_Counters`)
  - Optional creation of a production entry (`DBT_Productions`)
- **SharePoint provisioning module** under `/provisioning` that creates required lists/columns.

## Quick start

### 1) Provision SharePoint lists (one-time)
See: `/provisioning/docs/INSTALL.md`

### 2) Run the program
Prereqs: .NET 8 SDK

```powershell
cd .\src\DbtSharePointSample
copy appsettings.example.json appsettings.json
# edit appsettings.json (TenantId, ClientId, SiteUrl)
dotnet restore
dotnet run
```

## Folder overview
- `src/` – the program
- `provisioning/` – SharePoint list creation scripts/docs
- `scripts/` – convenience scripts (build, provision, publish)
- `docs/` – extra documentation

Generated: 2026-01-25T12:14:47
