# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Code Review Log

| Date | Branch | Findings |
|------|--------|----------|
| 2026-06-29 | `claude/claude-md-charges-review-1a2ser` | No issues detected — diff was empty (no committed or uncommitted changes relative to upstream). |

## What This Project Is

ProjectExplorer is a Windows Forms desktop app (.NET 10) that solves a real problem: instead of opening 10–15 File Explorer windows every day to navigate a project's scattered folders and web resources, you open one tool. It organizes projects as a hierarchy of **Collections**, **FolderReferences** (real disk folders), and **WebResources** (URLs) — bringing local folders and project-related web resources together in one place.

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
```

## Architecture

Three-layer solution:

| Project | Role |
|---|---|
| `ProjectExplorer.Core` | Models, ProjectManager service, IProjectRepository interface, JSON persistence |
| `ProjectExplorer.Shell` | Windows Shell P/Invoke wrappers (icons via `SHGetFileInfo`) |
| `ProjectExplorer.WinForms` | TreeView + ListView UI, dialogs, entry point |

### Data Model

```
Project
  └─ Collection (nestable)
       ├─ Collection
       ├─ FolderReference  (path to real folder on disk)
       └─ WebResource      (URL)
```

All children inherit from `ProjectChild` (Id, ParentId, SortOrder, Metadata dictionary). The `ChildType` enum discriminator drives polymorphic JSON deserialization in `JsonProjectRepository` — it avoids `System.Text.Json` converter issues by deserializing manually via `JsonNode`.

### Data Flow

User action in `MainForm` → `ProjectManager` (async CRUD) → `JsonProjectRepository.SaveAllAsync()` → `%APPDATA%\ProjectExplorer\projects.json` (with `.bak` backup on each save).

### Key Files

- `src/ProjectExplorer.Core/Services/ProjectManager.cs` — all business logic; start here for any feature work
- `src/ProjectExplorer.Core/Services/JsonProjectRepository.cs` — persistence; manual JSON node traversal for polymorphic deserialization
- `src/ProjectExplorer.Core/Models/Project.cs` — root model with tree helpers (`FindCollection`, `FindParentList`, circular reference detection)
- `src/ProjectExplorer.WinForms/Forms/MainForm.cs` — 1000+ line main window; TreeView (left) drives ListView (right) with navigation history stacks
- `src/ProjectExplorer.Shell/Services/ShellIconProvider.cs` — Windows-only icon retrieval; shell32.dll P/Invoke in `Interop/ShellNativeMethods.cs`
- `tests/ProjectExplorer.Tests/ProjectManagerTests.cs` — 16 xUnit tests; good reference for expected behavior of CRUD operations and tree traversal

### Adding a New Child Type

The pattern established by `WebResource` (commit `257e642`) is: add model → add `ChildType` enum value → handle in `JsonProjectRepository` deserialization switch → add `ProjectManager` CRUD methods → wire up context menu and dialog in `MainForm` → add tests.

## Distribution / Installer

The app targets Windows desktop users. Distribution plan:

- **Packaging**: Publish as a self-contained single-file executable via `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true`. Output goes to `publish/`.
- **Installer**: Use [Inno Setup](https://jrsoftware.org/isinfo.php) (free, well-established) to wrap the published exe into a standard Windows installer (`ProjectExplorerSetup.exe`). The Inno Setup script lives at `installer/ProjectExplorer.iss`.
- **No store dependency**: Ship as a direct download — no Microsoft Store submission required for initial release.
- **Auto-update**: Not planned for v1; revisit after initial user feedback.

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

## Planned Feature: Open Command Prompt Here

Right-click on any **FolderReference** (or a Collection that contains folder references) and choose **"Open CMD here"** / **"Open PowerShell here"** to launch a terminal pre-`cd`'d to that folder.

### Implementation notes

- Context menu item added in `MainForm.cs` for `FolderReference` nodes (both TreeView right-click and ListView right-click).
- Launch via `Process.Start(new ProcessStartInfo { FileName = "cmd.exe", WorkingDirectory = folderRef.Path, UseShellExecute = true })`.
- For PowerShell: swap `FileName` for `"powershell.exe"` (or `"pwsh.exe"` if installed).
- Guard with `Directory.Exists(folderRef.Path)` before launching; show a friendly error if the path no longer exists.
- Consider a settings option for preferred shell (CMD vs PowerShell) — store in `%APPDATA%\ProjectExplorer\settings.json`.
- No new model changes needed; purely a UI/shell layer addition.

### Tests to add

- Unit test that `ProjectManager` is not involved (shell launch is fire-and-forget in the UI layer — no service test needed).
- Manual test: verify working directory is correct, verify graceful error when path is missing.

## Planned Feature: Drag-and-Drop Node Moving

The TreeView needs to support dragging any node (Collection, FolderReference, WebResource) and dropping it onto a different parent Collection or a Project root to reparent it.

### Core service method needed

`ProjectManager` needs a `MoveChildAsync(Guid projectId, Guid childId, Guid? newParentCollectionId)` method:

1. Locate the child via `project.FindParentList(childId)` — remove it from the source list.
2. Resolve the destination list: `project.FindCollection(newParentCollectionId)?.Children` or `project.Children` for project root.
3. Guard against circular references when moving a Collection into one of its own descendants — use `project.HasCircularReferences()` after tentatively inserting, then roll back if true.
4. Set `child.ParentId = newParentCollectionId ?? project.Id` and append to destination list.
5. Save via `_repository.SaveProjectAsync(project)`.

### WinForms wiring (MainForm.cs)

- Set `treeView.AllowDrop = true`.
- Handle `ItemDrag` → call `treeView.DoDragDrop(e.Item, DragDropEffects.Move)`.
- Handle `DragEnter` → set `e.Effect = DragDropEffects.Move` when the data is a `TreeNode`.
- Handle `DragOver` → auto-scroll and highlight the node under the cursor via `treeView.GetNodeAt(...)`.
- Handle `DragDrop` → extract the dragged node's tag (e.g. `"Collection:<guid>"`), parse the target node's tag for the new parent id, call `await MoveChildAsync(...)`, then rebuild the affected tree branches.
- Reject drops that would move a Project node or drop onto a non-Collection target (FolderReference, WebResource).

### Tests to add

- Move a Collection into a sibling Collection.
- Move a FolderReference to the project root.
- Reject moving a Collection into its own descendant (circular reference guard).
- Verify `ParentId` is updated correctly after move.

## Roadmap

Roughly ordered by value vs. effort. Items marked **Near** are well-scoped and unambiguously useful; **Far** items need more user signal before committing to them.

### Near-term

| Feature | Notes |
|---|---|
| **Open CMD / PowerShell here** | Right-click on FolderReference → launch terminal in that folder. See planned feature section above. |
| **Drag-and-drop node moving** | Reparent any child by dragging within the TreeView. See planned feature section above. |
| **Reveal in Explorer** | Right-click FolderReference → `Process.Start("explorer.exe", path)`. Two-liner, high value. |
| **Copy path to clipboard** | Right-click FolderReference or WebResource → copy path/URL. Trivial to add. |
| **Import from clipboard / text** | Paste a folder path or URL and have ProjectExplorer auto-create the right child type. |
| **Keyboard navigation** | Arrow keys in TreeView already work; add Enter to open, F2 to rename, Del to delete. |

### Medium-term

| Feature | Notes |
|---|---|
| **Search / filter** | Filter the TreeView or ListView by name across all projects. Critical once collections grow large. |
| **Recently opened** | Track last-accessed folders/URLs per project session; surface in a "Recent" panel. |
| **Folder watcher** | Flag FolderReferences whose paths no longer exist (drive unmounted, folder renamed). |
| **Export / share a project** | Export a project definition as a `.peproj` JSON file to hand off to a colleague. |
| **Multiple windows / tabs** | Power users with dual monitors or many projects open simultaneously. |
| **Localization (i18n)** | Externalize all UI strings to `.resx` resource files — the standard .NET pattern. WinForms supports this well via `System.ComponentModel` and the Visual Studio resource designer. Worth doing before the codebase grows further; retrofitting i18n onto 1000+ line forms is significantly harder than starting clean. Target languages TBD based on user feedback, but European and East Asian languages are the most likely first candidates given the creative/technical professional audience. |

### Far-term (needs user validation first)

| Feature | Notes |
|---|---|
| **File preview pane** | Show thumbnails or text preview for the selected folder's contents inline. High complexity, benefit depends on persona. |
| **Cloud sync** | Sync `projects.json` via OneDrive / Dropbox path. Simple if users manage it themselves; complex if we build sync. |
| **Plugin / extension model** | Allow third-party child types (e.g. "Git repo" with branch info). Premature until core is stable. |
| **macOS / Linux port** | Requires replacing WinForms + Shell P/Invoke. Revisit if demand emerges from non-Windows users. |
