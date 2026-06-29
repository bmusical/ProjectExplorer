# build-installer.ps1 — run from the repo root or the installer\ folder
# Requires: .NET 10 SDK, Inno Setup 6 (iscc.exe on PATH or at default install path)

param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent

Write-Host "==> Publishing ProjectExplorer $Version (win-x64, self-contained)..." -ForegroundColor Cyan

dotnet publish "$repoRoot\src\ProjectExplorer.WinForms\ProjectExplorer.WinForms.csproj" `
    /p:PublishProfile=win-x64-release `
    /p:Version=$Version `
    -c Release

if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed"; exit 1 }

# Locate iscc.exe
$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    $iscc = "C:\Program Files (x86)\Inno Setup 6\iscc.exe"
    if (-not (Test-Path $iscc)) {
        Write-Error "iscc.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php"
        exit 1
    }
}

Write-Host "==> Running Inno Setup compiler..." -ForegroundColor Cyan

$issFile = "$PSScriptRoot\ProjectExplorer.iss"
& $iscc "/DAppVersion=$Version" $issFile

if ($LASTEXITCODE -ne 0) { Write-Error "Inno Setup compilation failed"; exit 1 }

Write-Host "==> Done! Installer is in installer\installer-output\" -ForegroundColor Green
