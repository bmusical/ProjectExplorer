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
4. **Run the release script**, which tags, pushes, and watches the build for you in one go —
   no other git or GitHub UI steps needed:
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
     --notes-file release-notes.md
   ```
   `release-notes.md` is a scratch file (gitignored) — delete it after, or just leave it for next
   time. The uploaded asset filename **must exactly match** the `<url>` this produces in
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
