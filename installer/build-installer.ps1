# build-installer.ps1 — run from the repo root or the installer\ folder
# Requires: .NET 10 SDK, Inno Setup 6 (iscc.exe on PATH or at default install path)
#
# Usage:
#   .\build-installer.ps1 -Version 1.2.0
#   .\build-installer.ps1 -Version 1.2.0 -UpdateXml   # also updates updates\updates.xml
#
# Only pass -UpdateXml once the GitHub Release for this version already exists with the
# installer attached — updates.xml is what the in-app auto-updater reads, publicly, so
# pointing it at a version before that version is actually downloadable means any installed
# copy checking for updates in that window 404s. See docs/RELEASE.md's manual path.

param(
    [string]$Version   = "1.0.0",
    [switch]$UpdateXml          # pass to bump updates\updates.xml — only after the release exists; see above
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent

# ── 1. Publish ────────────────────────────────────────────────────────────────

Write-Host "==> Publishing Project Nest Explorer $Version (win-x64, self-contained)..." -ForegroundColor Cyan

dotnet publish "$repoRoot\src\ProjectExplorer.WinForms\ProjectExplorer.WinForms.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishReadyToRun=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:Version=$Version `
    /p:PublishDir="$repoRoot\publish\\"

if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed"; exit 1 }

# ── 2. Inno Setup ─────────────────────────────────────────────────────────────

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    $iscc = "C:\Program Files (x86)\Inno Setup 6\iscc.exe"
    if (-not (Test-Path $iscc)) {
        Write-Error "iscc.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php"
        exit 1
    }
}

Write-Host "==> Running Inno Setup 6 compiler..." -ForegroundColor Cyan

$issFile  = "$PSScriptRoot\ProjectExplorer.iss"
$setupExe = "$PSScriptRoot\installer-output\ProjectNest-$Version-Setup.exe"

& $iscc "/DAppVersion=$Version" $issFile
if ($LASTEXITCODE -ne 0) { Write-Error "Inno Setup compilation failed"; exit 1 }

Write-Host "==> Installer: $setupExe" -ForegroundColor Green

# ── 3. Update updates.xml (optional) ─────────────────────────────────────────

if ($UpdateXml)
{
    $xmlPath = "$repoRoot\updates\updates.xml"
    Write-Host "==> Updating $xmlPath ..." -ForegroundColor Cyan

    $downloadUrl  = "https://github.com/bmusical/ProjectExplorer/releases/download/$Version/ProjectNest-$Version-Setup.exe"
    $changelogUrl = "https://github.com/bmusical/ProjectExplorer/releases/tag/$Version"

    $xml = [xml](Get-Content $xmlPath -Encoding UTF8)
    $xml.item.version   = $Version
    $xml.item.url       = $downloadUrl
    $xml.item.changelog = $changelogUrl
    $xml.Save($xmlPath)

    Write-Host "    version   : $Version"
    Write-Host "    url       : $downloadUrl"
    Write-Host "    changelog : $changelogUrl"
}

# ── 4. Summary ────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "==> Build complete!" -ForegroundColor Green
Write-Host ""
if ($UpdateXml) {
    Write-Host "updates\updates.xml now points at $Version - that only belongs on master once the" -ForegroundColor Yellow
    Write-Host "release below already exists with its asset attached. If it doesn't yet, commit/push" -ForegroundColor Yellow
    Write-Host "this file separately, after step 2." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Next steps for a release:"
    Write-Host "  1. Create GitHub Release: $Version (if not already done)"
    Write-Host "  2. Upload $setupExe as the release asset (if not already done)"
    Write-Host "  3. Commit and push updates\updates.xml"
    Write-Host "  4. Existing users will be prompted to update on next launch"
} else {
    Write-Host "Next steps for a release:"
    Write-Host "  1. Test/sign $setupExe as needed"
    Write-Host "  2. Commit and push the version bump (not updates.xml yet)"
    Write-Host "  3. Create GitHub Release: $Version, with $setupExe as the release asset"
    Write-Host "  4. Re-run this script with -UpdateXml, then commit/push updates\updates.xml"
    Write-Host "  5. Existing users will be prompted to update on next launch"
}
