# build-installer.ps1 - run from the repo root or the installer\ folder
# Requires: .NET 10 SDK, Inno Setup 6 (iscc.exe on PATH or at default install path)
#
# Usage:
#   .\build-installer.ps1 -Version 1.2.0
#   .\build-installer.ps1 -Version 1.2.0 -UpdateXml   # also updates updates\updates.xml
#   .\build-installer.ps1 -Version 1.2.0 -Sign        # also code-signs both exes with signtool
#
# Only pass -UpdateXml once the GitHub Release for this version already exists with the
# installer attached - updates.xml is what the in-app auto-updater reads, publicly, so
# pointing it at a version before that version is actually downloadable means any installed
# copy checking for updates in that window 404s. See docs/RELEASE.md's manual path.
#
# -Sign requires a code-signing certificate already usable by signtool.exe's "/a" (automatic
# best-certificate) selection - see docs/LAUNCH_CHECKLIST.md Section 6. If you're on Certum
# SimplySign, open the signing session (~2hr window) on your phone BEFORE running this, or the
# signtool calls below will hang/fail waiting for an approval that never comes.
#
# Prefer .\installer\cut-release.ps1 instead if you just want to cut a normal release -
# it handles tagging and publishing for you via the GitHub CLI. This script is for
# building/testing the installer locally (e.g. before code-signing).

param(
    [string]$Version   = "1.0.0",
    [switch]$UpdateXml,         # pass to bump updates\updates.xml - only after the release exists; see above
    [switch]$Sign               # pass to code-sign publish\ProjectNest.exe and the installer exe via signtool
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

function Invoke-NativeQuiet {
    param([Parameter(Mandatory = $true)][scriptblock]$ScriptBlock)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $ScriptBlock
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Find-SignTool {
    $onPath = Get-Command signtool -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    # signtool.exe ships with the Windows SDK, not on PATH by default - look under the usual
    # install root and take the newest bin\<sdk-version>\x64 copy found.
    $sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $sdkRoot) {
        $found = Get-ChildItem -Path $sdkRoot -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\x64\*" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    return $null
}

function Invoke-CodeSign {
    param([Parameter(Mandatory = $true)][string]$SignToolPath, [Parameter(Mandatory = $true)][string]$Path)

    Write-Host "==> Signing $Path ..." -ForegroundColor Cyan
    Invoke-NativeQuiet {
        & $SignToolPath sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a $Path
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Error "signtool failed to sign $Path. If you're on Certum SimplySign, confirm the ~2hr phone-approval signing session is still open."
        exit 1
    }
}

if ($Sign) {
    $signToolPath = Find-SignTool
    if (-not $signToolPath) {
        Write-Error "signtool.exe not found. Install the Windows SDK (https://developer.microsoft.com/windows/downloads/windows-sdk/), or add it to PATH."
        exit 1
    }
    Write-Host "==> Code signing enabled ($signToolPath)." -ForegroundColor Cyan
    Write-Host "    If you're on Certum SimplySign, make sure the ~2hr signing session is already open on your phone." -ForegroundColor Cyan
}

# --- 1. Publish ---

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

$publishedExe = "$repoRoot\publish\ProjectNest.exe"
if ($Sign) { Invoke-CodeSign -SignToolPath $signToolPath -Path $publishedExe }

# --- 2. Inno Setup ---

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

if ($Sign) { Invoke-CodeSign -SignToolPath $signToolPath -Path $setupExe }

Write-Host "==> Installer: $setupExe" -ForegroundColor Green

# --- 3. Update updates.xml (optional) ---

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
    $testStep = if ($Sign) { "Test $setupExe (already signed)" } else { "Test/sign $setupExe as needed" }
    Write-Host "Next steps for a release:"
    Write-Host "  1. $testStep"
    Write-Host "  2. Commit and push the version bump (not updates.xml yet)"
    Write-Host "  3. Create GitHub Release: $Version, with $setupExe as the release asset"
    Write-Host "  4. Re-run this script with -UpdateXml, then commit/push updates\updates.xml"
    Write-Host "  5. Existing users will be prompted to update on next launch"
}
