# Cutting a Release

Quick reference for shipping a new version of Project Nest Explorer. This assumes one-time setup
(Inno Setup installed, production ECDSA keypair generated, repo made public, Gumroad configured)
is already done — see `docs/LAUNCH_CHECKLIST.md` for that setup and for the full context behind
each step below.

**`updates/updates.xml` is owned by the release workflow, not by you.** Don't hand-edit it as part
of the version bump. It's the file the in-app auto-updater reads, and the repo is public, so
committing it early would advertise a version before its installer exists — anyone whose app
checks for updates in that window gets a 404. `.github/workflows/release.yml` updates and commits
it itself, as its last step, gated on the GitHub Release (with the installer attached) already
existing.

## Steps (recommended: tag-triggered, via `.github/workflows/release.yml`)

1. **Bump the version** in `src/ProjectExplorer.WinForms/ProjectExplorer.WinForms.csproj`
   (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`).
2. **Update `CHANGELOG.md`** with a new `## [X.Y.Z] — YYYY-MM-DD` section.
3. **Commit and push** those two changes to `master`.
4. **Push the tag**, `X.Y.Z` (no `v` prefix — this repo standardized on bare version tags; a `v`
   prefix also won't match the workflow's trigger pattern, so it simply won't run):
   ```bash
   git tag X.Y.Z
   git push origin X.Y.Z
   ```
   (Or create the GitHub Release directly through the web UI with tag `X.Y.Z` targeting `master`
   and no files attached — publishing it creates the tag, which fires the same workflow. Title
   convention: `Project Nest Explorer X.Y.Z`.)
5. **Watch the Actions run.** It publishes the self-contained `win-x64` build, compiles the
   installer, creates/updates the GitHub Release with `ProjectNest-X.Y.Z-Setup.exe` attached, and
   only then commits `updates/updates.xml` pointing at this version.
6. **Verify the update path**: install an older version, launch it, and confirm it detects the new
   release, downloads, and upgrades cleanly. User data in
   `%APPDATA%\ProjectExplorer\projects.json` must survive the upgrade (the installer's
   `[UninstallDelete]` intentionally leaves user data alone).

This path skips testing on a clean VM and code-signing (see the manual path below if you need
either before the release goes public).

## Manual / fully offline path

Use this instead of the above if you need to test the installer on a clean VM or code-sign it
*before* anyone can download it — the tag-triggered workflow publishes the GitHub Release (and
therefore makes the installer downloadable) as soon as it's built.

1. **Bump the version** and **update `CHANGELOG.md`** as in steps 1–2 above.
2. **Build the installer:**
   ```powershell
   .\installer\build-installer.ps1 -Version X.Y.Z
   ```
   This publishes a self-contained `win-x64` single-file exe to `publish\ProjectNest.exe` and runs
   Inno Setup to produce `installer-output\ProjectNest-X.Y.Z-Setup.exe`. Omit `-UpdateXml` here —
   see the callout above for why.
3. **Test the installer** on a clean Windows VM with no .NET installed, to confirm the
   self-contained build actually runs standalone. Verify the app icon shows correctly on the
   taskbar, Start menu shortcut, and Add/Remove Programs.
4. **(Recommended) Code-sign** the exe and installer with `signtool` if you have a code-signing
   certificate — see `docs/LAUNCH_CHECKLIST.md` Section 6. Unsigned exes trigger SmartScreen
   warnings that hurt conversion.
5. **Commit and push** the version bump (`updates/updates.xml` is *not* part of this commit).
6. **Create the GitHub Release**, tagged `X.Y.Z`:
   ```bash
   gh release create X.Y.Z \
     "installer-output/ProjectNest-X.Y.Z-Setup.exe" \
     --title "Project Nest Explorer X.Y.Z" \
     --notes-file docs/release-notes/X.Y.Z.md
   ```
   The uploaded asset filename **must exactly match** the `<url>` this produces in
   `updates/updates.xml` (`ProjectNest-X.Y.Z-Setup.exe`) or auto-update downloads will break.
7. **Only now**, re-run the build script with `-UpdateXml` (or hand-edit `updates/updates.xml`) to
   point at the release you just published, and commit/push that on its own:
   ```powershell
   .\installer\build-installer.ps1 -Version X.Y.Z -UpdateXml
   ```
8. **Verify the update path** as in step 6 of the recommended path above.

## Notes

- The repo must stay **public** — the in-app updater reads
  `https://raw.githubusercontent.com/bmusical/ProjectExplorer/master/updates/updates.xml`, which
  404s on a private repo.
- Keep `<mandatory>` in `updates/updates.xml` set to `false` unless you're shipping a critical fix
  that must not be skippable.
- If you're minting a new customer license key as part of this release cycle, that's a separate,
  unrelated flow — see `tools/KeyGen/README.md`.
