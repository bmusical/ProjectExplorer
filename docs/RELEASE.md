# Cutting a Release

Quick reference for shipping a new version of Project Nest Explorer. This assumes one-time setup
(Inno Setup installed, production ECDSA keypair generated, repo made public, Gumroad configured)
is already done — see `docs/LAUNCH_CHECKLIST.md` for that setup and for the full context behind
each step below.

## Steps

1. **Bump the version** in `src/ProjectExplorer.WinForms/ProjectExplorer.WinForms.csproj`
   (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`).
2. **Update `CHANGELOG.md`** with a new `## [X.Y.Z] — YYYY-MM-DD` section, and write the GitHub
   release notes (can reuse the changelog entry).
3. **Build the installer:**
   ```powershell
   .\installer\build-installer.ps1 -Version X.Y.Z -UpdateXml -Sign
   ```
   This publishes a self-contained `win-x64` single-file exe to `publish\ProjectNest.exe`, runs
   Inno Setup to produce `installer-output\ProjectNest-X.Y.Z-Setup.exe`, and (because of
   `-UpdateXml`) rewrites `updates\updates.xml` with the new version and download URL. `-Sign`
   code-signs both the exe and the installer via `signtool` — see step 5.
4. **Test the installer** on a clean Windows VM with no .NET installed, to confirm the
   self-contained build actually runs standalone. Verify the app icon shows correctly on the
   taskbar, Start menu shortcut, and Add/Remove Programs.
5. **Code-sign** the exe and installer by passing `-Sign` to `build-installer.ps1` (already covered
   in step 3) — see `docs/LAUNCH_CHECKLIST.md` Section 6. Requires Certum SimplySign Desktop
   installed and a signing session approved from the SimplySign mobile app before the build runs.
   Unsigned exes trigger SmartScreen warnings that hurt conversion.
6. **Commit and push** the version bump and updated `updates/updates.xml`.
7. **Create the GitHub Release**, tagged `X.Y.Z` (no `v` prefix — this repo standardized on bare
   version tags):
   ```bash
   gh release create X.Y.Z \
     "installer-output/ProjectNest-X.Y.Z-Setup.exe" \
     --title "Project Nest Explorer X.Y.Z" \
     --notes-file docs/release-notes/X.Y.Z.md
   ```
   The uploaded asset filename **must exactly match** the `<url>` in `updates/updates.xml`
   (`ProjectNest-X.Y.Z-Setup.exe`) or auto-update downloads will break.
8. **Verify the update path**: install an older version, launch it, and confirm it detects the new
   release, downloads, and upgrades cleanly. User data in
   `%APPDATA%\ProjectExplorer\projects.json` must survive the upgrade (the installer's
   `[UninstallDelete]` intentionally leaves user data alone).

## Notes

- The repo must stay **public** — the in-app updater reads
  `https://raw.githubusercontent.com/bmusical/ProjectExplorer/master/updates/updates.xml`, which
  404s on a private repo.
- Keep `<mandatory>` in `updates/updates.xml` set to `false` unless you're shipping a critical fix
  that must not be skippable.
- If you're minting a new customer license key as part of this release cycle, that's a separate,
  unrelated flow — see `tools/KeyGen/README.md`.
