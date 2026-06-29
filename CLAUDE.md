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
