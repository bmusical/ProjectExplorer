# build-installer.ps1 - run from the repo root or the installer\ folder
# Requires: .NET 10 SDK, Inno Setup 6 (iscc.exe on PATH or at default install path)
#
# Usage:
#   .\build-installer.ps1 -Version 1.2.0
#   .\build-installer.ps1 -Version 1.2.0 -UpdateXml   # also updates updates\updates.xml
#   .\build-installer.ps1 -Version 1.2.0 -Sign        # also code-signs the exe + installer
#
# -Sign requires Certum SimplySign Desktop installed and a signing session approved from the
# SimplySign mobile app (~2hr window) before running — see docs/LAUNCH_CHECKLIST.md Section 6.

param(
    [string]$Version   = "1.0.0",
    [switch]$UpdateXml,        # pass to bump updates\updates.xml after a successful build
    [switch]$Sign              # pass to code-sign the published exe + installer via signtool
)

$ErrorActionPreference = "Stop"
# PowerShell (all versions, not just 7.3+) converts every line a native command writes to STDERR
# into an error record, and with $ErrorActionPreference = "Stop" that gets promoted into a
# terminating exception at the moment it's written -- before this ever reaches the explicit
# $LASTEXITCODE checks below. dotnet and iscc.exe can both write ordinary warnings/progress info to
# stderr on an otherwise successful run. Invoke-NativeQuiet temporarily relaxes
# $ErrorActionPreference to "Continue" around just the native call itself so that can't happen;
# $LASTEXITCODE is unaffected either way and stays the source of truth this script already checks.
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path $PSScriptRoot -Parent

function Invoke-CodeSign {
    param([string]$FilePath)

    $signtoolCmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    $signtool = if ($signtoolCmd) { $signtoolCmd.Source } else {
        Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $signtool) {
        Write-Error "signtool.exe not found (ships with the Windows SDK). Install it, or omit -Sign."
        exit 1
    }

    Write-Host "==> Signing $FilePath ..." -ForegroundColor Cyan
    & $signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a "$FilePath"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "signtool failed for $FilePath. Make sure Certum SimplySign Desktop is installed and a signing session is open (approve via the SimplySign mobile app)."
        exit 1
    }
}

# ── 1. Publish ────────────────────────────────────────────────────────────────

Write-Host "==> Publishing Project Nest Explorer $Version (win-x64, self-contained)..." -ForegroundColor Cyan

Invoke-NativeQuiet {
    dotnet publish "$repoRoot\src\ProjectExplorer.WinForms\ProjectExplorer.WinForms.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:PublishReadyToRun=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:Version=$Version `
        /p:PublishDir="$repoRoot\publish\\"
}

if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed"; exit 1 }

if ($Sign) {
    Invoke-CodeSign "$repoRoot\publish\ProjectNest.exe"
}

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

Invoke-NativeQuiet { & $iscc "/DAppVersion=$Version" $issFile }
if ($LASTEXITCODE -ne 0) { Write-Error "Inno Setup compilation failed"; exit 1 }

Write-Host "==> Installer: $setupExe" -ForegroundColor Green

if ($Sign) {
    Invoke-CodeSign $setupExe
}

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

# --- 4. Summary ---

Write-Host ""
Write-Host "==> Build complete!" -ForegroundColor Green
Write-Host ""
if ($Sign) {
    Write-Host "==> Signed: publish\ProjectNest.exe and $setupExe" -ForegroundColor Green
} else {
    Write-Host "==> Not signed — re-run with -Sign to code-sign the exe + installer (avoids SmartScreen warnings)." -ForegroundColor Yellow
}
Write-Host ""
Write-Host "Next steps for a release:"
Write-Host "  1. Commit and push updates\updates.xml"
Write-Host "  2. Create GitHub Release: $Version"
Write-Host "  3. Upload $setupExe as the release asset"
Write-Host "  4. Existing users will be prompted to update on next launch"
