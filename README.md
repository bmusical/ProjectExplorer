<p align="center">
  <img src="src/ProjectExplorer.WinForms/Assets/logo.png" alt="Project Nest Explorer" width="120">
</p>

<h1 align="center">Project Nest Explorer</h1>

<p align="center">All your projects, one place.</p>

---

Instead of opening 10–15 File Explorer windows every day to navigate a project's scattered
folders and web resources, open one tool. Project Nest Explorer organizes each project as a
hierarchy of **Collections**, real disk **folders**, individual **files**, and **web resource**
links — so the folders, files, and URLs you use for a project live together in one panel instead
of scattered across Explorer windows and browser bookmarks.

Built for developers, creative/CAD/game artists, freelancers, and anyone else who
context-switches between multiple projects and is tired of re-navigating the same folder trees
every session.

## Features

- Nestable **Collections** to organize a project however you think about it.
- **Folder**, **file**, and **web resource** references, each opened with one click via the
  Windows shell.
- Right-click a folder to open a Command Prompt or PowerShell there, reveal it in Explorer, or
  copy its path.
- Drag-and-drop to reparent anything in the tree.
- Thumbnail/icon views, an in-app image viewer, and Windows 11 Fluent-styled UI.

## Download

Grab the latest installer from [GitHub Releases](https://github.com/bmusical/ProjectExplorer/releases).
Project Nest Explorer is Windows-only (10 1809+ / 11).

Free to use for up to 3 projects and 25 folder/file/web references. A one-time license removes
that limit — see [blaznaccess.com/landing/project-nest](https://blaznaccess.com/landing/project-nest).

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Windows (WinForms).

```bash
# Build
dotnet build

# Run
dotnet run --project src/ProjectExplorer.WinForms/ProjectExplorer.WinForms.csproj

# Run tests
dotnet test
```

To build a distributable installer, see [`docs/RELEASE.md`](docs/RELEASE.md).

## Project layout

| Path | What's there |
|---|---|
| `src/ProjectExplorer.Core` | Models, business logic (`ProjectManager`), JSON persistence, licensing |
| `src/ProjectExplorer.Shell` | Windows Shell P/Invoke (icons, Fluent window styling) |
| `src/ProjectExplorer.WinForms` | The desktop app itself |
| `tools/KeyGen` | Internal license key generator — see [`tools/KeyGen/README.md`](tools/KeyGen/README.md) |
| `tests/ProjectExplorer.Tests` | xUnit test suite |
| `installer/` | Inno Setup installer script and build automation |
| `docs/` | User-facing help doc, release runbook, and launch checklist |

`CLAUDE.md` has a deeper architecture writeup, roadmap, and target-user notes for anyone
contributing to the codebase.

## Help

See [`docs/HELP.md`](docs/HELP.md) for a full walkthrough — same content as the in-app Help ▸
Help Contents… dialog (`F1`).

## Support

Questions or bugs: [support@blaznaccess.com](mailto:support@blaznaccess.com), or open a
[GitHub Issue](https://github.com/bmusical/ProjectExplorer/issues).

## License

© 2025 HxM Blazor Software LLC. All rights reserved. This repository is source-visible but not
open source — no license is granted to copy, modify, or redistribute the code. End users of the
app are bound by [`LICENSE-EULA.txt`](LICENSE-EULA.txt), shown during installation.
