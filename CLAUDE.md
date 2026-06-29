# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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
