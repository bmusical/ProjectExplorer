# Project Nest Explorer — Help

## Please read this first: what this app does *not* do

Project Nest Explorer only **organizes references** to folders, files, and web pages — it doesn't
own or manage the things themselves.

- Adding a folder, file, or URL to a project never moves, copies, renames, or modifies anything on
  disk (or on the web). It just remembers where it is.
- Removing something from a project (or deleting a whole Project/Collection) never deletes the
  real folder or file, and never affects any website. It only removes the reference — the pointer
  you organized here — from this app.
- The **only** thing Project Nest Explorer ever writes to your computer is one data file:
  `%APPDATA%\ProjectExplorer\projects.json` (plus a `.bak` backup copy made automatically on every
  save), which records the tree of references you've built. A separate small file,
  `%APPDATA%\ProjectExplorer\license.json`, stores your license state if you've registered.
  Nothing else — no other files, no registry changes, nothing on your real folders — is ever
  touched by normal use.

If you ever want to reset everything, back up your setup, or move to another computer: that one
JSON file (and its `.bak`) is the entire footprint. Deleting it gives you a clean slate; nothing
you organized inside it was ever more than a reference.

## Quick start

1. **File ▸ New Project…** (or `Ctrl+N`) to create your first project.
2. Right-click the project in the tree and choose **Add Folder…**, **Add File…**, or
   **Add Web Resource…** to start bringing in references. Use **New Collection…** to group related
   references (Collections can nest inside each other).
3. Click anything in the tree to browse it in the right-hand panel; double-click a folder, file, or
   web resource row to open/preview it.

## Core concepts

| Term | What it is |
|---|---|
| **Project** | The top-level container — one per thing you're working on. |
| **Collection** | A nestable grouping inside a project, for organizing references however you think about the work. Collections can contain other Collections. |
| **Folder Reference** | A pointer to a real folder on disk. |
| **File Reference** | A pointer to a single real file on disk, opened with whatever app Windows normally uses for it. |
| **Web Resource** | A URL. |

Every one of these is a *reference* — think of the whole tree as a set of labeled bookmarks, not a
place where files actually live.

## Navigating and previewing

- The **TreeView** (left panel) is the project hierarchy; selecting a Project, Collection, or
  Folder Reference lists its contents in the **ListView** (right panel).
- Selecting a **File Reference** shows an inline preview panel instead — images and common text
  formats render directly; anything else still offers **Open** and **Properties** buttons.
- Selecting a **Web Resource** shows an inline browser preview (via the WebView2 runtime) with an
  **Open in External Browser** button. If the WebView2 Runtime isn't installed, the panel shows a
  message with that same button instead of the preview.
- The toolbar's **Back / Forward / Up** buttons and address bar navigate real folders the same way
  Explorer does — they only affect what's displayed, never the folders themselves.
- **View ▸ Details / Extra Large Icons / Large Icons / Small Icons / List / Tile** switches how the
  ListView displays folder contents.

## Unavailable folders, files, and web resources

A Folder Reference, File Reference, or Web Resource can point at something that isn't reachable
right now — a network or removable drive got disconnected, a local file was moved or deleted, or a
website is temporarily down. When that happens, the item shows **greyed out with a strikethrough**
in both the tree and list, and its tooltip explains why:

- **Local disk** — not found; it was likely moved, renamed, or deleted. Use **Locate Folder…**/
  **Locate File…** on its right-click menu to point it at the new location, or remove it.
- **Network/removable drive or web resource** — not reachable right now, which may just be
  temporary. Project Nest Explorer automatically re-checks these every 20 seconds and the item
  reverts to normal as soon as it's reachable again — no action needed. If you know a resource is
  gone for good and don't want it checked anymore, use **Stop Auto-Retry** on its right-click menu
  (**Resume Auto-Retry** turns it back on).

Right-click an unavailable item any time for **Check Availability Now** to re-check immediately
instead of waiting for the next automatic retry.

## Everyday actions (right-click menu)

| On this item | You can |
|---|---|
| **Project** | New Collection…, Add Folder…/File…/Web Resource…, Rename, Edit Description…, Delete Project |
| **Collection** | New Sub-Collection…, Add Folder…/File…/Web Resource…, Rename, Edit Description…, Delete Collection |
| **Folder Reference** | Open in Explorer, Open Command Prompt Here, Open PowerShell Here, Copy Path, Properties, Edit Description…, Remove from Project *(+ Check Availability Now / Locate Folder… / Stop-Resume Auto-Retry when unavailable)* |
| **File Reference** | Open, Open Containing Folder, Copy Path, Properties, Edit…, Remove from Project *(+ Check Availability Now / Locate File… / Stop-Resume Auto-Retry when unavailable)* |
| **Web Resource** | Open in External Browser, Copy URL, Edit…, Remove from Project *(+ Check Availability Now / Stop-Resume Auto-Retry when unavailable)* |

"Remove from Project" and "Delete Project/Collection" only remove entries from your
`projects.json` tree — see the note at the top of this document.

You can also drag and drop items in the tree to reorganize them into different Collections;
this only changes their place in `projects.json`, same as any other edit here.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+N` | New Project… |
| `F2` | Rename the selected tree item |

## Free tier & licensing

Project Nest Explorer is free for up to **3 projects** and **25 folder/file/web references**
total (Collections themselves don't count against the limit). A one-time license key removes that
limit entirely.

- **Help ▸ Register / License…** to enter a key you've purchased, or to see your current free-tier
  usage.
- Purchase a license from the link in that dialog, or at
  [blaznaccess.com/landing/project-nest](https://blaznaccess.com/landing/project-nest).
- License verification happens fully offline — no account, no internet connection required to
  activate or keep using a registered key.

## Checking for updates

**Help ▸ Check for Updates…** checks for a newer version and offers to download it. Besides that
and loading Web Resource previews you've added, the only other automatic network activity is
checking whether a Web Resource is currently reachable (see **Unavailable folders, files, and web
resources** above) — everything else works fully offline.

## Getting help

Questions, bugs, or anything not covered here: **support@blaznaccess.com**, or open an issue at
[github.com/bmusical/ProjectExplorer](https://github.com/bmusical/ProjectExplorer/issues).
