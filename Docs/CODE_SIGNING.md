# Code signing and CI signing (guide)

This document explains how to export a code-signing certificate (.pfx), convert it to base64, create GitHub Actions secrets and test signing locally and in CI.

IMPORTANT: treat the .pfx and its password as highly sensitive material.

## 1) Export a .pfx (Windows)

If the certificate is installed in your CurrentUser personal store, you can export it with PowerShell:

```powershell
# Replace <THUMBPRINT> and PFX_PASSWORD
$thumbprint = '<THUMBPRINT>'
$pwd = ConvertTo-SecureString -String 'PFX_PASSWORD' -Force -AsPlainText
Export-PfxCertificate -Cert Cert:\CurrentUser\My\$thumbprint -FilePath C:\temp\signing.pfx -Password $pwd
```

Alternatively use the Certificates MMC snap-in (Certificates - Current User -> Personal -> Certificates), right-click the cert -> All Tasks -> Export -> include private key -> PFX.

## 2) Convert .pfx to base64 (for GitHub secret)

PowerShell (Windows):

```powershell
$b = [System.IO.File]::ReadAllBytes('C:\temp\signing.pfx')
$base64 = [Convert]::ToBase64String($b)
# Copy to clipboard
$base64 | Set-Clipboard
# OR write to file
$base64 | Out-File -Encoding ascii C:\temp\signing.pfx.b64
```

macOS / Linux:

```bash
base64 signing.pfx > signing.pfx.b64
```

## 3) Create GitHub Actions secrets

In the repository settings -> Secrets and variables -> Actions -> New repository secret, create the following secrets:

- `SIGNING_PFX_B64` — paste the base64 content of the `.pfx` file
- `SIGNING_PFX_PASS` — the PFX password (plain text)

Alternatively you can use the GitHub CLI (`gh`) to create secrets.

## 4) How CI uses these secrets (what the repo already contains)

The CI workflow `.github/workflows/build-and-pack.yml` includes a step that will decode `SIGNING_PFX_B64` and write `Installer/signing.pfx`, and sets environment variables `SIGNING_PFX` and `SIGNING_PFX_PASS`. The `Installer/Build-Installer.ps1` script will sign the generated MSI using `signtool` if those environment variables are present.

Example (already implemented in repo):

- Workflow step decodes PFX and writes it into the workspace
- `Build-Installer.ps1` runs `signtool sign /f "$env:SIGNING_PFX" /p "$env:SIGNING_PFX_PASS" /tr http://timestamp.digicert.com /td sha256 /fd sha256 "path\to\msi"`

## 5) Test signing locally

1. Ensure you have Windows SDK or `signtool.exe` available on PATH.
2. Run your build locally:

```powershell
# Publish
dotnet publish "DAISY-Braille Toolkit" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
# Create MSI via WiX locally or run the included script (requires WiX):
# .\Installer\Build-Installer.ps1 -ProjectPath "DAISY-Braille Toolkit"
# Or sign the EXE/MSI directly for testing:
signtool sign /f "C:\path\to\signing.pfx" /p "PFX_PASSWORD" /tr http://timestamp.digicert.com /td sha256 /fd sha256 "Installer\DAISY-Braille-Toolkit.msi"
```

Verify the signature:

```powershell
signtool verify /pa /v "Installer\DAISY-Braille-Toolkit.msi"
```

or use Explorer properties -> Digital Signatures tab.

## 6) Security recommendations

- Use an Organization secret or GitHub Environments for tighter control if multiple repositories need signing.
- Rotate the certificate periodically.
- For enterprise scenarios, consider using a signing service (Azure Key Vault + HSM) instead of storing a PFX in secrets.

## 7) Troubleshooting

- If `signtool` fails in CI, make sure the PFX is decoded correctly and that the password is correct.
- Ensure the Windows runner has the certificate tools (signtool) available — this repo's workflow uses the default Windows runners which include sign tools when Windows SDK components are present.

## Automate secret creation with GitHub CLI (gh)

You can create the repository secrets from a machine that has the GitHub CLI (`gh`) installed and authenticated. Below are example commands.

1) Prepare the base64 value on your machine (Windows PowerShell):

```powershell
$bytes = [System.IO.File]::ReadAllBytes('C:\path\to\signing.pfx')
$b64 = [Convert]::ToBase64String($bytes)
# write to file if you want to inspect
$b64 | Out-File -Encoding ascii C:\path\to\signing.pfx.b64
```

Or macOS / Linux:

```bash
base64 signing.pfx > signing.pfx.b64
```

2) Create secrets with `gh` (replace `OWNER/REPO` and paths):

```bash
# set repo variable
REPO=OWNER/REPO

# SIGNING_PFX_B64
gh secret set SIGNING_PFX_B64 --repo "$REPO" --body-file signing.pfx.b64

# SIGNING_PFX_PASS (type or pipe the password)
echo -n "your-pfx-password" | gh secret set SIGNING_PFX_PASS --repo "$REPO" --body -
```

Notes:
- `gh` must be authenticated as a user with permission to create repository secrets.
- For organization-level security you may prefer using Organization secrets or GitHub Environments with protection rules.
- After creating the secrets, the CI workflow will decode `SIGNING_PFX_B64` and make the PFX available to the build script as `SIGNING_PFX` with the password `SIGNING_PFX_PASS`.

If you want, I can also add a small helper script to the `Installer/` folder that performs the base64 conversion and optionally runs the `gh` commands (you must run it locally because it requires the PFX and authentication). Reply if you want that helper created.