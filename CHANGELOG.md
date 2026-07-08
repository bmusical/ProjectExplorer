# Changelog

All notable changes to Project Nest Explorer are documented here. Versions correspond to the
`<Version>` in `src/ProjectExplorer.WinForms/ProjectExplorer.WinForms.csproj` and to GitHub
Releases tagged `<version>` (no `v` prefix).

## [Unreleased]

- Fix the ListView showing a stale folder listing when the TreeView selection moves to a
  FileReference; add an inline preview panel (image/text formats) with Open/Properties buttons.
- Allow Projects and Collections to have a description.
- Unify TreeView/ListView context menus and add a Properties verb.
- Windows 11 Fluent styling via DWM/UxTheme P/Invoke (rounded corners, Mica/dark titlebar).
- Add `LICENSE-EULA.txt` and wire it into the installer as an Accept/Decline page (`LicenseFile`
  in `installer/ProjectExplorer.iss`).

## [1.0.3] — 2026-07-08

- Add CMD/PowerShell context menu items to real-folder TreeView nodes.
- Add toolbar buttons for CMD/PowerShell/Copy Path with 3D-styled icons.
- Standardize release tags without a `v` prefix.

## [1.0.2] — 2026-07-04

- Fill in publisher, support, and updates URLs in the Inno Setup script.
- Fix icons missing in non-image views; add Extra Large Icons view.
- Add image viewing: thumbnails in the folder view and an in-app image viewer.
- Add "Option B" thumbnails (icons in compact views) and Open CMD/PowerShell here.
- Add UI polish and a new File resource type (`FileReference`), plus ListView context menu.
- Replace the development ECDSA public-key placeholder with a real generated key.
- Show the running version number in the main window title bar.
- Fix the updater URL (`/main/` → `/master/`) and unify support/URLs on `blaznaccess.com`.
- Add the launch checklist runbook (`docs/LAUNCH_CHECKLIST.md`) and banner/logo polish.

## [1.0.1] — 2026-07-01

- Rename app display name to **Project Nest Explorer**.
- Fill in publisher, support, and updates URLs in the Inno Setup script.

## [1.0.0] — 2026-06-29

Initial release.

- Rename app to **Project Nest**.
- Add license/registration system: usage-based free tier (3 projects / 25 leaf references) plus
  ECDSA-signed key activation, replacing an earlier time-based trial design.
- Add the `tools/KeyGen` console app for generating and verifying license keys.
- Add AutoUpdater.NET integration and a `updates/updates.xml`-based release pipeline.
- Add the Inno Setup installer script and a win-x64 self-contained publish profile.
- Add an About form and Help menu, including Help ▸ Check for Updates.
- Establish the core data model: Projects containing nestable Collections, FolderReferences, and
  WebResources, persisted to `%APPDATA%\ProjectExplorer\projects.json`.
