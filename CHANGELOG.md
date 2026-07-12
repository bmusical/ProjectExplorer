# Changelog

All notable changes to Project Nest Explorer are documented here. Versions correspond to the
`<Version>` in `src/ProjectExplorer.WinForms/ProjectExplorer.WinForms.csproj` and to GitHub
Releases tagged `<version>` (no `v` prefix).

## [1.0.5] — 2026-07-12

- Flag unavailable Folder/File References and Web Resources (disconnected network/removable
  drives, moved/deleted local files, web resources whose site returns an HTTP error): grey +
  strikethrough styling with a tooltip explaining local-disk vs. network vs. web unavailability,
  automatic background re-checking of network/removable/web resources every 20 seconds, and new
  right-click actions — "Check Availability Now", "Locate Folder…/Locate File…" to relink a moved
  item, and "Stop/Resume Auto-Retry" to silence polling for a resource that's gone for good.
- Fix Web Resources showing the broken (grey/strikethrough) styling for links that were actually
  fine — a connection failure, DNS hiccup, or timeout no longer flags a link as unavailable, since
  those are just as likely a transient network blip as a dead link; only a confirmed HTTP error
  response from the site itself (a 404, a 500, etc.) does now, and it clears automatically the
  next time the site loads successfully.
- Fix a crash-on-exit (`Font.ToHfont()` / `ArgumentException` in `TreeView.CustomDraw`) that could
  happen after using the app long enough for an unavailable-resource strikethrough font to be
  created, then closing the app.
- F2 now also renames the selected Project/Collection from the ListView, not just the TreeView.
- Add a manual "Refresh" to every Web Resource's right-click menu regardless of its current
  status, not just when already flagged unavailable — useful both as a workaround for a site that
  keeps failing the automated check (e.g. one that blocks non-browser requests) even though it
  loads fine in a real browser, and simply as a normal, everyday way to double-check a link's
  status any time, for any reason.

## [1.0.4] — 2026-07-11

- Add drag-drop conversion between Projects and Collections in the TreeView, plus tree reordering
  with a live insertion-line indicator; fix gaps that could appear during drag-drop reordering.
- Add Project reordering and Move Up/Down context-menu commands.
- Add a "Focus on Run" setting.
- Add a Help ▸ Help Contents… dialog (`F1`) summarizing core concepts, everyday actions, keyboard
  shortcuts, licensing, and — up front — what the app does *not* do (it only manages references;
  the only file it ever writes is `projects.json`). Mirrors the new `docs/HELP.md`.
- Add an inline WebResource preview: selecting a WebResource tree node renders its URL via
  WebView2 in place of the ListView, and its ListView row now does the same on double-click
  (instead of launching the external browser directly). Both the tree/list context menus and the
  preview panel offer an explicit "Open in External Browser" action. Requires the WebView2
  Runtime; falls back to a message + external-browser button when it's not installed.
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
