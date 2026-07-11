# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Code Review Log

| Date | Branch | Findings |
|------|--------|----------|
| 2026-06-29 | `claude/claude-md-charges-review-1a2ser` | No issues detected — diff was empty (no committed or uncommitted changes relative to upstream). |
| 2026-07-08 | `claude/claude-md-review-2r5x3l` | Two doc-drift issues fixed: (1) CLAUDE.md's Licensing section claimed `docs/LAUNCH_CHECKLIST.md`'s pre-flight checkbox for the public-key replacement was still stale, but the checklist already had it checked off — the stale claim was in CLAUDE.md itself, not the checklist. (2) `docs/LAUNCH_CHECKLIST.md`'s "Quick reference" section used a `vX.Y.Z` tag in its `gh release create` example, contradicting the no-`v`-prefix tag convention stated everywhere else (same doc §5.2, `docs/RELEASE.md`, `CHANGELOG.md`). Everything else checked (README, CHANGELOG, RELEASE.md, KeyGen README, LicenseManager.cs, MainForm.cs keyboard handling) matched the code. |

## What This Project Is

**Project Nest Explorer** (repo/codebase name: ProjectExplorer; publisher: HxM Blazor Software LLC) is a Windows Forms desktop app (.NET 10) that solves a real problem: instead of opening 10–15 File Explorer windows every day to navigate a project's scattered folders and web resources, you open one tool. It organizes projects as a hierarchy of **Collections**, **FolderReferences** (real disk folders), **FileReferences** (individual files), and **WebResources** (URLs) — bringing local folders, files, and project-related web resources together in one place.

It ships as a commercial product: a free tier (3 projects / 25 leaf references) gated by an offline, ECDSA-signed license key system, sold via Gumroad. See **Licensing & Distribution** below.

### Naming: Product vs. Program

**"Project Nest"** is the product/brand — deliberately one notch broader than this app, leaving room to add other tools under the same brand later. **"Project Nest Explorer"** is the name of *this* program specifically. Don't collapse the two:

- **Product** (`Project Nest`) shows up as: the `<Product>` MSBuild property in `ProjectExplorer.WinForms.csproj` (the exe's Win32 "Product name" metadata), and the small bold "PROJECT NEST" eyebrow label shown above the program name in `AboutForm.cs`, `RegistrationDialog.cs`, and `MainForm.Designer.cs`'s header band.
- **Program** (`Project Nest Explorer`) is everything else: the main window title bar (`MainForm.SetWindowTitle`), dialog titles, the bigger title label under each "PROJECT NEST" eyebrow, `AssemblyName`/`AppExeName` derivatives (`ProjectNest.exe`), README/CHANGELOG/installer copy, etc.
- The name also doubles as a bit of wordplay: read with the emphasis on "Project" (as in *a project*, i.e. a noun describing the nest) it's a workspace; read with the emphasis on "Nest" it reads as a project's name; some pronounce "Project" like *projector* either way.

`docs/LAUNCH_CHECKLIST.md` §1 tracks this same Product/Program split as a one-time naming-consistency checkbox.

## Commands

```bash
# Build
dotnet build

# Run (Windows only — WinForms)
dotnet run --project src/ProjectExplorer.WinForms/ProjectExplorer.WinForms.csproj

# Run all tests
dotnet test

# Run a single test by name
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Publish a self-contained single-file exe (produces publish/ProjectNest.exe)
dotnet publish src/ProjectExplorer.WinForms -r win-x64 --self-contained true -p:PublishSingleFile=true

# Build the Inno Setup installer end-to-end (Windows only, requires Inno Setup 6)
./installer/build-installer.ps1 -Version X.Y.Z -UpdateXml
```

## Architecture

Three app layers plus an internal key-generation console tool:

| Project | Role |
|---|---|
| `ProjectExplorer.Core` | Models, `ProjectManager` service, `IProjectRepository` interface, JSON persistence, `LicenseManager` |
| `ProjectExplorer.Shell` | Windows Shell P/Invoke wrappers (icons via `SHGetFileInfo`; Fluent/Mica window styling via DWM/UxTheme) |
| `ProjectExplorer.WinForms` | TreeView + ListView UI, dialogs, entry point. Assembly name `ProjectNest`, product `Project Nest`, program `Project Nest Explorer` (see **Naming** above) |
| `tools/KeyGen` | Standalone console app — generates the ECDSA keypair and signs customer license keys. Internal use only, never shipped to customers |

### Data Model

```
Project (has optional Description)
  └─ Collection (nestable, has optional Description)
       ├─ Collection
       ├─ FolderReference  (path to real folder on disk)
       ├─ FileReference    (path to a single real file on disk; opened via its associated app)
       └─ WebResource      (URL)
```

All children inherit from `ProjectChild` (Id, ParentId, SortOrder, Metadata dictionary). The `ChildType` enum discriminator drives polymorphic JSON deserialization in `JsonProjectRepository` — it avoids `System.Text.Json` converter issues by deserializing manually via `JsonNode`.

### Data Flow

User action in `MainForm` → `ProjectManager` (async CRUD) → `JsonProjectRepository.SaveAllAsync()` → `%APPDATA%\ProjectExplorer\projects.json` (with `.bak` backup on each save). License state is stored separately at `%APPDATA%\ProjectExplorer\license.json` via `LicenseManager`; tree expand/select state at `uisettings.json`; app preferences (Focus on Run) and window bounds at `appsettings.json` via `AppSettingsManager`.

### Key Files

- `src/ProjectExplorer.Core/Services/ProjectManager.cs` — all business logic; start here for any feature work
- `src/ProjectExplorer.Core/Services/JsonProjectRepository.cs` — persistence; manual JSON node traversal for polymorphic deserialization
- `src/ProjectExplorer.Core/Services/LicenseManager.cs` — free-tier limit checks + ECDSA license key verification; see Licensing section
- `src/ProjectExplorer.Core/Models/Project.cs` — root model with tree helpers (`FindCollection`, `FindParentList`, circular reference detection)
- `src/ProjectExplorer.Core/Services/AppSettingsManager.cs` / `Models/AppSettings.cs` — app-wide preferences (Focus on Run, window bounds), persisted at `appsettings.json`
- `src/ProjectExplorer.WinForms/Forms/MainForm.cs` — 2300+ line main window; TreeView (left) drives ListView (right) with navigation history stacks, drag-and-drop reparenting/reordering/conversion, and unified context menus
- `src/ProjectExplorer.WinForms/Forms/SettingsForm.cs` — File ▸ Settings… dialog (currently just Focus on Run)
- `src/ProjectExplorer.WinForms/Helpers/SingleInstanceGuard.cs` — named Mutex + EventWaitHandle pair backing "Prevent multiple copies"
- `src/ProjectExplorer.Shell/Services/WindowActivator.cs` — `AttachThreadInput`/`SetForegroundWindow` P/Invoke wrapper used to reliably steal foreground focus across processes (`Interop/WindowActivationNativeMethods.cs`)
- `src/ProjectExplorer.WinForms/Forms/RegistrationDialog.cs` — license key activation UI (Help ▸ Register / License…)
- `src/ProjectExplorer.WinForms/Forms/HelpForm.cs` — in-app Help ▸ Help Contents… dialog (`F1`); content mirrors `docs/HELP.md`, keep both in sync when either changes
- `src/ProjectExplorer.WinForms/Forms/ImageViewerForm.cs` — in-app image viewer for image FileReferences/folder contents
- `src/ProjectExplorer.WinForms/Forms/FilePreviewPanel.cs` — inline preview panel shown in place of the ListView when a FileReference tree node is selected; renders image/text content when supported, always offers Open/Properties otherwise
- `src/ProjectExplorer.Core/Services/FilePreviewHelper.cs` — classifies a file path as Image/Text/None for `FilePreviewPanel`, shared with `ImageFileHelper`
- `src/ProjectExplorer.WinForms/Forms/WebResourcePreviewPanel.cs` — inline preview panel shown in place of the ListView when a WebResource tree node is selected (a ListView row for one routes here too, via `SelectTreeNodeByTag`); renders the URL with WebView2 (`Microsoft.Web.WebView2` package — requires the WebView2 Runtime, falls back to a message + "Open in External Browser" if it's missing), always offers "Open in External Browser"
- `src/ProjectExplorer.Shell/Services/ShellIconProvider.cs` — Windows-only icon retrieval; shell32.dll P/Invoke in `Interop/ShellNativeMethods.cs`
- `src/ProjectExplorer.Shell/Services/ModernWindowStyler.cs` — Windows 11 Fluent/dark-mode window styling via DWM P/Invoke in `Interop/DwmNativeMethods.cs`
- `tools/KeyGen/Program.cs` — `setup` / `generate` / `verify` commands for the license key system
- `tests/ProjectExplorer.Tests/ProjectManagerTests.cs` — xUnit tests for CRUD operations and tree traversal
- `tests/ProjectExplorer.Tests/ImageViewingTests.cs` — xUnit tests for image detection/viewing behavior
- `tests/ProjectExplorer.Tests/FilePreviewHelperTests.cs` — xUnit tests for `FilePreviewHelper`'s Image/Text/None classification
- `docs/HELP.md` — user-facing help doc (what the app does/doesn't do, concepts, everyday actions, shortcuts, licensing); mirrored by the in-app `HelpForm.cs` dialog
- `docs/LAUNCH_CHECKLIST.md` — the authoritative, start-to-finish runbook for licensing, packaging, Gumroad, and releases; consult it before touching anything in the Licensing or Distribution sections below
- `docs/RELEASE.md` — condensed "cut a new release" steps, extracted from the checklist above
- `tools/KeyGen/README.md` — how to generate the ECDSA keypair and mint/verify customer license keys
- `README.md` — public-facing repo overview; `CHANGELOG.md` — version history

### Adding a New Child Type

The pattern established by `WebResource` and `FileReference` is: add model → add `ChildType` enum value → handle in `JsonProjectRepository` deserialization switch → add `ProjectManager` CRUD methods → wire up context menu and dialog in `MainForm` → add tests.

## Licensing & Distribution

The app is freemium: **Free = 3 projects, 25 leaf references** (Collections don't count); a license key unlocks unlimited use. Enforced by `LicenseManager` in `ProjectExplorer.Core`.

- **Key format**: ECDSA P-256 signed payloads of the form `email|FULL|yyyy-MM-dd`, encoded as `base64url(payload).base64url(signature)`. Verification is fully offline against an embedded public key — no license server.
- **`tools/KeyGen`** is the key factory: `dotnet run -- setup` generates the keypair once (guard `private_key.pem` like cash, never commit it); `dotnet run -- generate --email <buyer>` mints a customer key per sale; `dotnet run -- verify --license <key>` sanity-checks one.
- **Dev-mode placeholder — already replaced**: `LicenseManager.PublicKeyPem` used to ship as `"DEVELOPMENT_KEY_PLACEHOLDER"` (which makes the app accept *any* correctly-formatted string as a valid license), but the real ECDSA public key was embedded in commit `5a95f73` (2026-07-02). Verification is live, not dev mode. `docs/LAUNCH_CHECKLIST.md`'s pre-flight checkbox for this is already checked off, in sync with the code.
- **Packaging**: `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true` → self-contained single-file `publish/ProjectNest.exe`.
- **Installer**: [Inno Setup 6](https://jrsoftware.org/isinfo.php) wraps the exe into `ProjectNest-<version>-Setup.exe`. Script at `installer/ProjectExplorer.iss`; fully scripted via `installer/build-installer.ps1`.
- **Auto-update**: Wired up via `Autoupdater.NET.Official`, reading `updates/updates.xml` from the repo's `master` branch on GitHub (requires the repo to be public).
- **Sales channel**: Gumroad (handles checkout/tax); recommended model is signed keys generated per-sale via `tools/KeyGen`, not Gumroad's own random license keys (incompatible with the ECDSA verifier).
- **No store dependency**: direct download, no Microsoft Store submission.

`docs/LAUNCH_CHECKLIST.md` is the full runbook (brand/legal, keypair generation, Gumroad setup, release steps, code signing) — treat it as the source of truth and keep it in sync with any change to licensing or the release process.

## Target Users

Understanding who uses this shapes which features matter most:

| Persona | Pain point ProjectExplorer solves |
|---|---|
| **Software developers** | Many projects, each with source dirs, build output, docs, deployment folders, and reference URLs scattered across drives |
| **Graphic / photo / video artists** | Large asset libraries across multiple drives; project folders, reference image folders, client delivery folders all need fast access |
| **CAD / 3D / game artists** | Deep folder hierarchies for assets, textures, exports; switching between multiple client projects daily |
| **Freelancers (any discipline)** | Per-client project trees that mix local folders and web resources (briefs, portals, shared drives) |
| **Power users / IT professionals** | Admin toolkits, server paths, runbook URLs — one organized panel instead of bookmarks + Explorer windows |

Common thread: **people who context-switch between multiple projects** and hate re-navigating the same folder trees every session.

## Recently Shipped

These were tracked as "Planned Feature" sections in earlier versions of this file — both are now fully implemented, so treat the code as the source of truth rather than the notes below:

- **Open CMD / PowerShell here** — `MainForm.LaunchTerminal(folderPath, usePowerShell)` (`MainForm.cs`), wired into toolbar buttons (`BtnOpenCmd_Click` / `BtnOpenPowerShell_Click`) and the TreeView/ListView context menus for `FolderReference` nodes.
- **Reveal in Explorer** — `Process.Start("explorer.exe", "/select,\"<path>\"")`, on both FolderReference and FileReference context menus.
- **Copy path to clipboard** — `Clipboard.SetText(path)` on FolderReference/FileReference/WebResource context menus.
- **Drag-and-drop node moving, reordering, and conversion** — `ProjectManager.MoveChildAsync(Guid projectId, Guid childId, Guid? newParentCollectionId, Guid? beforeSiblingId = null)` (`ProjectManager.cs`) handles reparenting *and* same/cross-container reordering (position given by sibling Id rather than index, so it can't drift when the source removal shifts indices) plus the circular-reference guard. `MainForm.cs`'s `ComputeDropPlan`/`GetDropZone` decide, per row-third the cursor is over, whether to reparent-into (middle third of a Collection/Project), reorder-before/after (top/bottom third, shown with `ControlPaint.DrawReversibleLine` as an insertion line), or trigger one of two type conversions: dragging a Project onto a Collection calls `ProjectManager.ConvertProjectToCollectionAsync` (project becomes a collection nested there, Id preserved so children stay valid); dragging a Collection onto the "Projects" root node calls `ConvertCollectionToProjectAsync` (collection becomes a new top-level project, Id preserved). Valid drop containers remain Project/Collection only.
- **Focus on Run (single instance)** — File ▸ Settings… (`SettingsForm.cs`) sets `AppSettings.FocusOnRun` (`AppSettingsManager`, `appsettings.json`). "Prevent multiple copies" makes `Program.cs` take a named Mutex via `SingleInstanceGuard`; a later "Prevent"-mode launch that finds it already held signals the running instance (`EventWaitHandle`) and exits instead of opening a window. The running instance's `SingleInstanceGuard.ListenForActivation` callback marshals onto the UI thread and calls `MainForm.RestoreAndActivate()`, which uses `ProjectExplorer.Shell.Services.WindowActivator` (P/Invoke `AttachThreadInput` + `SetForegroundWindow`) since plain `Form.Activate()` is often ignored across processes. "Allow multiple copies" launches never touch the mutex at all, so they're invisible to it either way. Window position/size is persisted on close (`MainForm.SaveWindowBounds`) and restored on launch; `MainForm.EnsureVisibleOnScreen` resets it to a centered position on the primary screen if it no longer falls on any connected screen — this runs on every startup and on every "Prevent"-mode refocus, regardless of which Focus on Run mode is active.
- **Unified context menus + Properties verb** — TreeView and ListView right-click menus were consolidated; a Properties dialog was added for inspecting/editing node metadata.
- **Windows 11 Fluent styling** — `ModernWindowStyler` applies DWM/UxTheme attributes (rounded corners, Mica/dark titlebar) at startup.
- **F2 rename** — `MainForm.TreeView_KeyDown` handles `Keys.F2` on the selected TreeView node. (Del-to-delete and Enter-to-open in the TreeView are **not** wired up yet — only the address bar handles Enter today; still open items, see Roadmap.)
- **FileReference inline preview** — selecting a FileReference tree node now shows `FilePreviewPanel` in place of the ListView instead of leaving the previously-viewed folder listing on screen. Renders images and common text formats inline (`FilePreviewHelper.GetPreviewKind`); always offers Open/Properties buttons regardless of whether the format is previewable.
- **WebResource inline preview** — selecting a WebResource tree node shows `WebResourcePreviewPanel` in place of the ListView, rendering the URL inline via WebView2; double-clicking a WebResource row in the ListView now selects the corresponding tree node (same as Project/Collection/FolderRef) so it shows in the same preview instead of launching the external browser directly. Both the TreeView/ListView context menus and the preview panel itself offer an explicit "Open in External Browser" action.
- **In-app Help** — Help ▸ Help Contents… (`F1`) opens `HelpForm`, a scrollable dialog covering core concepts, everyday actions, keyboard shortcuts, and licensing — leading with an explicit "what this app does NOT do" section (it only manages references; everything it writes lives under `%APPDATA%\ProjectExplorer\`: `projects.json`, `license.json`, `uisettings.json`, `appsettings.json`). Content is mirrored in `docs/HELP.md`, which is also linked from the README.

## Roadmap

Roughly ordered by value vs. effort. Items marked **Near** are well-scoped and unambiguously useful; **Far** items need more user signal before committing to them.

### Near-term

| Feature | Notes |
|---|---|
| **Make the repo public** | ⛔ Ship-blocker, not a feature: the license public key is already replaced (see Licensing & Distribution above), but the repo still needs to be public for the in-app auto-updater to reach `updates/updates.xml` on `raw.githubusercontent.com`. See `docs/LAUNCH_CHECKLIST.md` Section 0 & 5.1. |
| **Keyboard navigation (remaining)** | F2 rename and address-bar Enter already work; still need Enter-to-open and Del-to-delete on the TreeView/ListView selection. |
| **Import from clipboard / text** | Paste a folder path or URL and have ProjectExplorer auto-create the right child type. |
| **Localization (i18n) scaffolding** | Externalize all UI strings to `.resx` resource files before the codebase grows further. Establish `Strings.resx` (default/English) and the `.Designer.cs` accessor pattern now — every string added from day 1 goes in the right place. Actual translations added incrementally as language demand is confirmed. WinForms supports this natively; no third-party library needed. |

### Medium-term

| Feature | Notes |
|---|---|
| **Search / filter** | Filter the TreeView or ListView by name across all projects. Critical once collections grow large. |
| **Recently opened** | Track last-accessed folders/URLs per project session; surface in a "Recent" panel. |
| **Folder watcher** | Flag FolderReferences whose paths no longer exist (drive unmounted, folder renamed). |
| **Export / share a project** | Export a project definition as a `.peproj` JSON file to hand off to a colleague. |
| **Multiple windows / tabs** | Power users with dual monitors or many projects open simultaneously. |

### Far-term (needs user validation first)

| Feature | Notes |
|---|---|
| **File preview pane** | Show thumbnails or text preview for the selected folder's contents inline. High complexity, benefit depends on persona. |
| **Cloud sync** | Sync `projects.json` via OneDrive / Dropbox path. Simple if users manage it themselves; complex if we build sync. |
| **Plugin / extension model** | Allow third-party child types (e.g. "Git repo" with branch info). Premature until core is stable. |
| **macOS / Linux port** | Requires replacing WinForms + Shell P/Invoke. Revisit if demand emerges from non-Windows users. |
