# Project Nest Explorer — Help

## Please read this first: what this app does *not* do

Project Nest Explorer only **organizes references** to folders, files, and web pages — it doesn't
own or manage the things themselves.

- Adding a folder, file, or URL to a project never moves, copies, renames, or modifies anything on
  disk (or on the web). It just remembers where it is.
- Removing something from a project (or deleting a whole Project/Collection) never deletes the
  real folder or file, and never affects any website. It only removes the reference — the pointer
  you organized here — from this app.
- Everything Project Nest Explorer writes to your computer lives in `%APPDATA%\ProjectExplorer\`:
  `projects.json` (plus a `.bak` backup copy made automatically on every save) records the tree of
  references you've built; `license.json` stores your license state if you've registered;
  `uisettings.json` remembers which tree items were expanded/selected; `appsettings.json` stores
  app preferences (like Focus on Run) and the main window's last position/size. Nothing else — no
  other files, no registry changes, nothing on your real folders — is ever touched by normal use.

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

A Folder Reference or File Reference can point at something that's gone missing — a network or
removable drive got disconnected, or a local file was moved or deleted. A Web Resource shows this
only when the site itself actively returns an error (a 404, a 500, etc.) — never just because a
check couldn't connect, since that's just as likely a passing network blip as a dead link, and
flagging it either way would make perfectly working links flash broken. When one of these happens,
the item shows **greyed out with a strikethrough** in both the tree and list, and its tooltip
explains why:

- **Local disk** — not found; it was likely moved, renamed, or deleted. Use **Locate Folder…**/
  **Locate File…** on its right-click menu to point it at the new location, or remove it.
- **Network/removable drive** — not reachable right now, which may just be temporary. Project
  Nest Explorer automatically re-checks these every 20 seconds and the item reverts to normal as
  soon as it's reachable again — no action needed.
- **Web resource** — the site returned an error the last time it was checked. Web resources
  aren't polled in the background — click **Refresh** on its right-click menu to check again, and
  it reverts to normal as soon as the page loads successfully.

If you know a network/removable drive is gone for good and don't want it checked anymore, use
**Stop Auto-Retry** on its right-click menu (**Resume Auto-Retry** turns it back on). Web
resources have no auto-retry to stop — they're only ever checked when shown or refreshed.

Right-click an unavailable Folder/File Reference any time for **Check Availability Now** to
re-check immediately instead of waiting for the next automatic retry. A Web Resource's equivalent
is labeled **Refresh**, and it's always on its right-click menu — not just when the item is
flagged unavailable. That's partly for the case where a site keeps failing the automated check
(e.g. one that blocks non-browser requests) even though it loads fine for you, but it's really
just a normal, everyday action: click **Refresh** on any Web Resource any time you want to
double-check its status, for any reason.

## Everyday actions (right-click menu)

| On this item | You can |
|---|---|
| **Project** | New Collection…, Add Folder…/File…/Web Resource…, Rename, Edit Description…, Move Up/Down, Delete Project |
| **Collection** | New Sub-Collection…, Add Folder…/File…/Web Resource…, Rename, Edit Description…, Move Up/Down, Delete Collection |
| **Folder Reference** | Open in Explorer, Open Command Prompt Here, Open PowerShell Here, Copy Path, Properties, Edit Description…, Move Up/Down, Remove from Project *(+ Check Availability Now / Locate Folder… / Stop-Resume Auto-Retry when unavailable)* |
| **File Reference** | Open, Open Containing Folder, Copy Path, Properties, Edit…, Move Up/Down, Remove from Project *(+ Check Availability Now / Locate File… / Stop-Resume Auto-Retry when unavailable)* |
| **Web Resource** | Open in External Browser, Copy URL, Edit…, Move Up/Down, Remove from Project, Refresh |

"Remove from Project" and "Delete Project/Collection" only remove entries from your
`projects.json` tree — see the note at the top of this document.

You can also drag and drop items in the tree to reorganize them: drop onto a Collection or
Project to move something inside it, or drop just above/below a row (watch for the insertion
line) to reorder it relative to its siblings without changing its parent — this includes
Projects themselves, dragged onto one another. Two special gestures convert between the two
container types instead of just moving something: drag a Project onto a Collection to turn
that project into a collection nested there, or drag a Collection onto the "Projects" root to
turn it into its own top-level project (its contents come along either way). If the exact drop
position is fiddly to hit, every item's right-click menu also has **Move Up**/**Move Down**,
which does the same repositioning without needing to drag at all. All of this only changes
placement in `projects.json`, same as any other edit here. (Reparenting via drag stays within
the same Project — moving something into a *different* project isn't supported yet except via
the two conversion gestures above.)

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+N` | New Project… |
| `F2` | Rename the selected Project or Collection (works from either the tree or the list) |

## Settings

**File ▸ Settings…** currently has one option, **Focus on Run**:

- **Prevent multiple copies** (default) — launching the app while it's already running switches
  to the existing window instead of opening a second one.
- **Allow multiple copies** — every launch opens its own independent window.

Either way, if the main window's last saved position has drifted off every screen you currently
have connected (for example, it was on a second monitor that's since been unplugged), it's
automatically moved back onto your primary screen the next time it becomes visible.

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
