# cut-release.ps1 - one-command release cutter for Project Nest Explorer.
#
# Wraps the whole "tag it, push it, watch it build" flow so you don't have to type
# raw git commands or babysit the GitHub Actions tab in a browser tab. This assumes
# the version bump (csproj <Version> + a CHANGELOG.md section) is ALREADY committed
# and pushed to master - this script only tags and watches, it never bumps the
# version or touches updates.xml itself (the release workflow does that once the
# release actually exists - see docs/RELEASE.md).
#
# Requires: git, and the GitHub CLI (gh) - https://cli.github.com/ - already
# authenticated (run "gh auth login" once if you haven't).
#
# Usage:
#   .\installer\cut-release.ps1 -Version 1.0.4

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
# PowerShell (all versions, not just 7.3+) converts every line a native command writes to STDERR
# into an error record, and with $ErrorActionPreference = "Stop" that gets promoted into a
# terminating exception -- at the moment the native command writes it, before a redirect like
# "*> $null" or "2>$null" on the same line ever gets a chance to discard it. gh and git both
# routinely write normal, successful-run output to stderr (gh auth status always does, even when
# logged in; git fetch/push write progress lines there too), which was aborting this script before
# it could even reach its own $LASTEXITCODE checks below. Invoke-NativeQuiet below temporarily
# relaxes $ErrorActionPreference to "Continue" around just the native call itself so that stderr
# chatter can't terminate the script; $LASTEXITCODE is untouched either way and stays the one
# source of truth this script already checks explicitly after every call that matters.
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot

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

try {
    # --- 0. Sanity checks ---

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Error "GitHub CLI (gh) not found. Install it from https://cli.github.com/, run 'gh auth login', then try again."
        exit 1
    }

    Invoke-NativeQuiet { gh auth status *> $null }
    if ($LASTEXITCODE -ne 0) {
        Write-Error "gh is not authenticated. Run 'gh auth login' first."
        exit 1
    }

    Write-Host "==> Fetching latest master..." -ForegroundColor Cyan
    Invoke-NativeQuiet { git fetch origin master }
    if ($LASTEXITCODE -ne 0) { Write-Error "git fetch failed"; exit 1 }

    $csprojPath = Join-Path $repoRoot "src\ProjectExplorer.WinForms\ProjectExplorer.WinForms.csproj"
    $csprojMatch = Select-String -Path $csprojPath -Pattern "<Version>(.*)</Version>"
    $csprojVersion = $csprojMatch.Matches[0].Groups[1].Value

    if ($csprojVersion -ne $Version) {
        Write-Error "csproj <Version> is '$csprojVersion', not '$Version'. Bump it (and add a CHANGELOG.md section), commit, and push to master first - see docs/RELEASE.md."
        exit 1
    }

    $changelogPath = Join-Path $repoRoot "CHANGELOG.md"
    $hasChangelogSection = Select-String -Path $changelogPath -Pattern "^## \[$([regex]::Escape($Version))\]" -Quiet
    if (-not $hasChangelogSection) {
        Write-Error "CHANGELOG.md has no '## [$Version]' section yet. Add one, commit, and push to master first."
        exit 1
    }

    $localMaster = Invoke-NativeQuiet { git rev-parse master 2>$null }
    $remoteMaster = Invoke-NativeQuiet { git rev-parse origin/master }
    if ($localMaster -and $localMaster -ne $remoteMaster) {
        Write-Error "Your local master doesn't match origin/master. Push your version bump commit to master first, then re-run this script."
        exit 1
    }

    $existingTag = Invoke-NativeQuiet { git ls-remote --tags origin "refs/tags/$Version" }
    if ($existingTag) {
        Write-Error "Tag $Version already exists on origin. If a previous attempt half-finished, delete that tag/release on GitHub first, or pick a new version."
        exit 1
    }

    # --- 1. Tag origin/master and push ---

    Write-Host "==> Tagging $Version at origin/master and pushing..." -ForegroundColor Cyan
    Invoke-NativeQuiet { git tag $Version origin/master }
    Invoke-NativeQuiet { git push origin $Version }
    if ($LASTEXITCODE -ne 0) { Write-Error "Pushing the tag failed"; exit 1 }

    Write-Host "==> Tag pushed. Waiting for the Release workflow to start..." -ForegroundColor Cyan
    Start-Sleep -Seconds 10

    # --- 2. Watch the workflow run live, right here in the terminal ---

    $runId = Invoke-NativeQuiet { gh run list --workflow=release.yml --branch=$Version --limit=1 --json databaseId --jq ".[0].databaseId" }

    if (-not $runId) {
        Write-Host "Could not find the run automatically yet. Check it here:" -ForegroundColor Yellow
        Write-Host "  https://github.com/bmusical/ProjectExplorer/actions/workflows/release.yml"
    }
    else {
        Write-Host "==> Watching run $runId (this takes a few minutes - Windows build + Inno Setup)..." -ForegroundColor Cyan
        Invoke-NativeQuiet { gh run watch $runId --exit-status }
        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "==> The workflow run failed. Logs:" -ForegroundColor Red
            Write-Host "  https://github.com/bmusical/ProjectExplorer/actions/runs/$runId"
            exit 1
        }
    }

    # --- 3. Summary ---

    Write-Host ""
    Write-Host "==> Done! Release:" -ForegroundColor Green
    Write-Host "  https://github.com/bmusical/ProjectExplorer/releases/tag/$Version"
}
finally {
    Pop-Location
}
