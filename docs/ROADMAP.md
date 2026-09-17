# Project Nest Roadmap

This file captures the long-term, major-version direction for Project Nest and Project Nest Explorer. For the near/medium-term feature list, see `CLAUDE.md`.

## Version 1 (current)

- Windows Forms desktop app for organizing project references (Projects → Collections → FolderReferences / FileReferences / WebResources).
- SQLite default storage with safe migration from legacy JSON.
- Offline ECDSA license-key verification.
- Auto-update via GitHub Releases and `updates/updates.xml`.
- Comprehensive search, drag-and-drop, broken/unavailable reference handling, and Windows 11 Fluent styling.

## Version 2

Version 2 focuses on going online, making sharing between nests successful end to end, and providing the operations users expect from an alternative Explorer shell.

### Going online and sharing Nest Eggs

Nest Eggs are packages of information that can be shared from one nest to another. Version 2 covers both the packages and the supporting capabilities needed to make that sharing useful to the recipient, not just exporting a file.

The online architecture, package contents, delivery/import flow, and handling of references to resources on another machine remain to be scoped. The relationship to the earlier `.peproj` single-project handoff idea should be resolved as part of that work rather than assuming separate formats. The existing `File ▸ Export All My Data...` remains a one-user personal-data export, not the sharing workflow.

### Deeper Explorer integration

Extend the Explorer-style behavior further, including copy/paste and other operations expected in an alternative Explorer shell. The exact operation set and implementation approach to Explorer subclassing remain to be defined. These capabilities go beyond Version 1's reference-only organization, so operations on real files must be clearly distinguished from operations on nest references.

### Minimal plugin interface trial (tentative)

Broader plugin support is deferred to Version 3. Version 2 may establish a small plugin interface and one basic plugin, such as **Copy To**, to exercise reference access and core functionality through a simple, practical task. Use that experience to inform the later plugin architecture rather than building the full system up front. The Copy To plugin's exact behavior remains to be defined.

### Shell extension DLLs

Provide Windows Explorer shell DLLs so users can interact with Project Nest directly from File Explorer — for example, right-clicking a folder or file to add it to a nest, or opening a nest from Explorer.

### Web browser extensions (exploratory)

Explore browser extensions that let users add web resources to a nest directly from the browser.

## Version 3

Broader plugin support, including backups and specialized features, is deferred here from Version 2. A possible minimal Version 2 plugin trial should inform that design. The rest of Version 3 will take shape as we make decisions about Version 2 and brainstorm; its scope is intentionally open rather than fixed in advance.

## Engine / UX separation: planning inventory

### Scope and preservation

Planning pass dated 2026-09-07, on branch `planning/engine-ux-boundaries`, based on commit `82ba88f` plus the existing working-tree changes. This inventory describes the code on disk, not necessarily the released Gumroad binary. Git reported modified application files and untracked files when the branch was created; those were left untouched. Creating the branch preserves the committed `master` history, but is not a backup or commit of those local edits.

This is a functional inventory, grouping related methods rather than listing every constructor, drawing helper, or generated event subscription. **Existing methods** below were found in source; **proposed ownership, contracts, and notifications** are planning recommendations, not implemented APIs. No application code is changed by this pass. No UI framework, network transport, or macOS release is selected.

Working direction: retain WinForms as the first functioning client of a reusable engine, then prove a modern UX against the same application operations. Modernizing UX is not contingent on finishing all online/sharing features. Before implementation, confirm the authoritative checkout and checkpoint the intended local changes separately from this plan.

### Responsibility model

| Boundary | Owns | Must not own |
|---|---|---|
| **UX / presentation** | Layout, tree/list rendering, selection, navigation history, dialogs, confirmation wording, localization, keyboard/drag gestures, accessibility, preview controls, window state, UI dispatch | Direct mutation of shared domain objects, authoritative license/validation rules, database writes |
| **Application / engine** | Typed operations and queries, project/reference rules, command-time validation and limits, coordinated persistence, consistent state snapshots, change notifications; reusable availability policy | Forms, message boxes, `TreeNode`/`ListViewItem`, HWNDs, `Bitmap`, browser controls, UI synchronization contexts |
| **Infrastructure** | SQLite/legacy JSON, migration, storage locations, HTTP checks, filesystem enumeration/content access, archive writing | Dialogs, navigation selection, assumptions that a remote client's paths belong to the server |
| **Platform / host adapters** | File-manager/terminal launching, native properties/icons/thumbnails, clipboard, pickers, app lifecycle, platform activation, embedded-browser integration, installer/updater | Business rules or independent copies of the same writable nest state |

These are responsibility boundaries, not a mandate to create four new projects immediately. Extend the existing Core and Shell separation incrementally. Keep consumer-facing contracts free of Windows types; Windows-only adapter contracts may retain native handles internally.

### Source map

Paths are relative to this document. Method names in the tables refer to these files unless a different class is named.

| Key | Source |
|---|---|
| PM | [ProjectManager.cs](../src/ProjectExplorer.Core/Services/ProjectManager.cs) |
| Models | [Core models](../src/ProjectExplorer.Core/Models) |
| Core services | [Core services](../src/ProjectExplorer.Core/Services) |
| Repository | [IProjectRepository.cs](../src/ProjectExplorer.Core/Interfaces/IProjectRepository.cs) |
| MF | [MainForm.cs](../src/ProjectExplorer.WinForms/Forms/MainForm.cs) |
| Designer | [MainForm.Designer.cs](../src/ProjectExplorer.WinForms/Forms/MainForm.Designer.cs) |
| Forms | [WinForms forms and preview panels](../src/ProjectExplorer.WinForms/Forms) |
| Host | [Program.cs](../src/ProjectExplorer.WinForms/Program.cs) and [Helpers](../src/ProjectExplorer.WinForms/Helpers) |
| Shell | [Shell interfaces](../src/ProjectExplorer.Shell) and [implementations](../src/ProjectExplorer.Shell/Services) |
| Tests | [ProjectExplorer.Tests](../tests/ProjectExplorer.Tests) |

### A. Nest operations and state

The response/notification column is **proposed**, except where explicitly described as existing. Ordinary commands return an awaited result; notifications tell other subscribers about committed changes, not whether the caller's command succeeded.

| Functionality | Existing methods / location | Engine or infrastructure responsibility | UX responsibility | Proposed response / notification |
|---|---|---|---|---|
| Initialize workspace | PM `InitializeAsync`; Host `RunApplication`; `ProjectStoreMigrator.ResolveRepository` | Load/migrate through repository; expose initialization result and initial snapshot | Loading/error experience; host supplies storage location | Initialization result with diagnostics; initial revision |
| Project CRUD | PM `CreateProjectAsync`, `GetProject`, `RenameProjectAsync`, `UpdateProjectAsync`, `DeleteProjectAsync`; MF `MenuFileNewProject_Click`, `PerformRenameAsync` | Validate, enforce limits, mutate and persist project | Gather name/description, confirm removal, render results | Command result; `WorkspaceChanged` |
| Collection CRUD | PM `CreateCollectionAsync`, `RenameCollectionAsync`, `UpdateCollectionAsync`, `DeleteCollectionAsync`; MF `MenuProjectNewCollection_Click` | Nesting rules and persistence | Input, display, confirmation | Command result; `WorkspaceChanged` |
| Folder references | PM `AddFolderReferenceAsync`, `UpdateFolderReferenceAsync`, `RemoveFolderReferenceAsync`; MF `MenuProjectAddFolder_Click`, `LocateFolderReferenceAsync` | Store reference/description and validate update; relinking is not moving a real folder | Folder picker, explain missing path, choose replacement | Command result; change notification; invalidate availability for old path |
| File references | PM `AddFileReferenceAsync`, `UpdateFileReferenceAsync`, `RemoveFileReferenceAsync`; MF `ShowAddFileResourceDialog`, `LocateFileReferenceAsync`; `FileResourceDialog` | Reference rules and persistence | File picker/editor and relink confirmation | Command result; change notification; invalidate old preview/check |
| Web references | PM `AddWebResourceAsync`, `UpdateWebResourceAsync`, `RemoveWebResourceAsync`; `WebResource.TryGetNavigableUri`; MF `ShowAddWebResourceDialog`; `WebResourceDialog` | URL normalization/validation rules; preserve `OpenExternalOnly` preference | URL editor; choose internal/external presentation | Command result; change notification |
| Reference/container removal | MF `DeleteByTagWithConfirmAsync`, `DeleteProjectWithConfirmAsync`, `DeleteCollectionWithConfirmAsync`, `RemoveFolderReferenceWithConfirmAsync`, `RemoveFileReferenceWithConfirmAsync`, `RemoveWebResourceWithConfirmAsync` | Execute typed removal only after request; revalidate current target | Confirmation and distinction between removing a reference and deleting a real file | Result with affected IDs; change notification |
| Metadata | PM `SetChildMetadataAsync` | Store metadata and validate recognized policy keys | Render editable fields and retry preference | Command result; change notification |
| Child order and reparenting | PM `ReorderChildrenAsync`, `MoveChildAsync`, `MoveChildUpAsync`, `MoveChildDownAsync` | Parent/order/cycle validation; persist changes | Gestures or Move Up/Down commands | Result with parent/order changes |
| Project order | PM `MoveProjectAsync`, `MoveProjectUpAsync`, `MoveProjectDownAsync` | Persist project ordering | Gesture/button selection | Workspace-order change notification |
| Project/collection conversion | PM `ConvertProjectToCollectionAsync`, `ConvertCollectionToProjectAsync` | Preserve identity/contents; enforce limits; commit all affected projects consistently | Explain conversion and submit typed intent | One logical command result/change set, not intermediate updates |
| Tree lookup and names | `Project.FindCollection`, `FindParentList`, `HasCircularReferences`; reference `EffectiveName`; MF `FindChildAnywhere` | Typed lookup and hierarchy rules; move reusable lookup out of MF | Localized labels and node construction | Query result; no event required |
| Search | `SearchService.Search`; `SearchForm.RunSearch`, `ActivateSelected`; MF `OpenSearchForm`, `OnSearchResultActivated` | Search consistent workspace data | Search field, result list, navigate to selected ID | Query result; rerun/invalidate on workspace change if needed |
| License policy and activation | `LicenseManager.GetCurrentLicense`, `CountLeafNodes`, `Activate`, `Deactivate`; MF `CheckLeafLimit`, `CheckProjectLimit`, `RefreshLicense`, `UpdateLicenseUi`; `RegistrationDialog.BtnActivate_Click` | Authoritative limit checks at command execution; offline verification; separate license storage access | Activation form, usage display, purchase link and explanatory prompts | Typed rejection/activation result; `LicenseStateChanged` |
| Persistence | Repository `LoadAllAsync`, `SaveAllAsync`, `SaveProjectAsync`, `DeleteProjectAsync`; `SqliteProjectRepository`, `JsonProjectRepository` | Storage adapters implement the engine-facing repository contract | No direct repository calls in views | Awaited success/failure; engine publishes only after commit |
| Migration | `ProjectStoreMigrator.ResolveRepository`; `SqliteProjectRepository.Checkpoint`, `EnsureSchema` | Preserve temporary-file migration and legacy fallback; expose meaningful diagnostics | Explain fallback/recovery without presenting an empty success | Initialization result; optional operation progress |
| Export personal data | `UserDataExportService.ExportAll`; MF `MenuFileExportMyData_Click` | Coordinate a consistent archive through storage/archive services | Save picker and completion/error display | Awaitable export result; progress/cancellation if added |

### B. UX, browsing, previews, and long-running work

| Functionality | Existing methods / location | Keep on UX side | Reusable service / mechanism to separate |
|---|---|---|---|
| Tree/list construction | MF `InitializeTreeView`, `AddProjectNode`, `AddChildNode`, `PopulateProjectList`, `PopulateProjectContents`, `PopulateCollectionContents`, `RefreshTreeView` | Controls, node mapping, expand/selection preservation | Consume read-only snapshots and typed IDs; do not expose WinForms `Tag` strings as engine identifiers |
| Selection and navigation | MF `TreeView_AfterSelect`, `ActivateByTag`, `SelectTreeNodeByTag`, `NavigateToPath`, `BtnBack_Click`, `BtnForward_Click`, `BtnUp_Click`, `AddressBar_KeyDown` | Selected item, back/forward stacks, address display; per-window session state | Resolve target by ID/path through queries; issue launch/read requests as appropriate. Selection is not a global domain event |
| Keyboard and menu actions | MF `TreeView_KeyDown`, `ListView_KeyDown`, `TreeView_AfterLabelEdit`, `RenameViaDialog`, `ShowTreeViewContextMenu`, `ShowListViewContextMenu`, `AddNewChildMenuItems`, type-specific `Add*MenuItems`; Designer wiring | Shortcuts, menu labels, dialog lifecycle, input accessibility | Typed command invocation plus capability/eligibility queries; actual execution must still enforce rules |
| Drag/drop | MF `TreeView_ItemDrag`, `TreeView_DragEnter`, `TreeView_DragOver`, `TreeView_DragLeave`, `TreeView_DragDrop`, `GetDropZone`, `ComputeDropPlan`, `DrawInsertionLine`, `ClearInsertionLine` | Coordinates, hit-testing, cursor/insertion-line feedback | Translate gesture to move/reorder/convert intent; engine independently validates ancestry, target, limits and current revision |
| Folder browsing | MF `PopulateFileList`, `TreeView_BeforeExpand`, `BuildFolderTreeData`, `PreloadLazyFoldersAsync` | Render directory entries and lazy placeholders | Cancellable directory reader returns data, not `TreeNode`; bounded enumeration with permission errors and link/cycle policy |
| Expand/collapse | MF `ExpandAllAsync`, `ExpandBranchAsync`, `BtnExpandAll_Click`, `BtnExpandBranch_Click`, `BtnCollapseAll_Click`, `BtnCollapseToTop_Click`, `BtnCollapseBranch_Click` | Expansion policy and presentation | Background enumeration where necessary; expansion alone must not edit the nest |
| Sorting/view modes | MF `SetViewMode`, `ListView_ColumnClick`; `ListViewColumnSorter.Compare` | Details/icons/list/tile, sort selection, column widths, formatting | Portable entry metadata; distinguish temporary view sorting from persisted project ordering |
| Availability monitoring | MF `EnsureAvailabilityChecked`, `ForceCheckAvailabilityAsync`, `CheckAvailabilityAsync`, `RecheckUnavailableResourcesAsync`; `ResourceAvailabilityChecker.ClassifyPath`, `CheckFolderAsync`, `CheckFileAsync`, `CheckWebResourceAsync` | Signal relevant resources, display status and explicit Refresh | Application monitor owns cache/retry/in-flight work; filesystem/HTTP adapters perform checks; emit `ResourceAvailabilityChanged` |
| Availability presentation and relink | MF `UpdateAvailabilityVisuals`, `ApplyAvailabilityStyle`, `BuildAvailabilityTooltip`, `AddAvailabilityMenuItems`, `LocateFolderReferenceAsync`, `LocateFileReferenceAsync` | Colors/fonts/tooltips; picker and menu state | Structured status/reason rather than preformatted UI text; retry suppression remains a persisted preference |
| File preview | MF `ShowFileReferencePreview`, `HideFileReferencePreview`; `FilePreviewPanel.ShowFile`, `ShowImagePreview`, `ShowTextPreview`, `ShowHtmlPreview`, `ShowMarkdownPreview`, `ShowInWebView`, `InitWebViewCoreAsync`; `FilePreviewHelper.GetPreviewKind` | Preview controls, fallback buttons, rendering and UI lifetime | Separate classification/content reading from renderer; impose size limits and cancellation; treat HTML/Markdown as untrusted content |
| Web preview | MF `ShowWebResourcePreview`, `HideWebResourcePreview`; `WebResourcePreviewPanel.ShowWebResource`, `Navigate`, `EnsureCoreAsync`, `NavigateCore`, `ShowUnavailable` | Embedded-browser control and external-only preference | Platform renderer; normalization stays reusable. Define navigation/security policy; missing runtime degrades to external launch |
| Image viewing | MF `OpenImageViewer`; `ImageViewerForm.ShowImage`, `Zoom`, `Rotate`; `ImageViewerModel.Next`, `Previous`; `ImageFileHelper.IsImageFile`, `IsImageExtension` | Zoom/rotation/display and viewer selection; do not confuse display rotation with file edits | Portable image classification; optional reusable presentation model, not a reason to move controls into the engine |
| Icons/thumbnails | MF `QueueThumbnail`, `GetFileRefImageIndex`, `GetImageIndex`, `GetFileRefListImageKey`; Shell providers | Image lists, scaling, UI insertion/disposal | Platform image provider; identify results by target/request and discard stale responses; define native and managed resource ownership |
| Progress and cancellation | `ProgressDialog.RunAsync<T>`; MF `PreloadLazyFoldersAsync`, `QueueThumbnail` | Progress dialog/inline indicator, Cancel button | Task result + `CancellationToken` + optional `IProgress<OperationProgress>`; no engine dependency on `IWin32Window`; closing a view cancels/detaches safely |
| Window and tree preferences | MF `ApplyPersistedWindowBounds`, `SaveWindowBounds`, `EnsureVisibleOnScreen`, `SaveTreeState`, `LoadTreeState`, `CaptureExpandedTags`, `CollectExpandedTags`, `RestoreTreeState`, `OnFormClosing`; `AppSettingsManager.Load`, `Save` | Window bounds, expansion, selection and client-specific preferences | Settings storage service; do not treat Windows monitor coordinates as shared nest data |
| Chrome/help/about | MF `SetWindowTitle`, `OnHandleCreated`, `UpdateAddressBar`, `UpdateStatusBar`, `UpdateToolbarButtons`; Designer; `HelpForm.BuildContent`, `AboutForm` | Branding, layout, fonts, accessible labels, help, dialogs | Structured application/version/status data; shared content may be reused, control layout cannot |

### C. Platform, host, and future integration surface

| Capability | Existing methods / status | Proposed boundary and cross-platform implications |
|---|---|---|
| Open file/folder/URL | MF `OpenFileReference`, `LaunchWebResource`, `BtnOpenExplorer_Click`; inline `Process.Start` in menus | Resource-launch adapter; Windows associations/Explorer, macOS equivalents, or browser-permitted actions. Accept validated targets, not arbitrary shell command strings |
| Open terminal | MF `LaunchTerminal`, `BtnOpenCmd_Click`, `BtnOpenPowerShell_Click` | Host advertises supported terminal choices; CMD/PowerShell options are not universal |
| Clipboard | MF `BtnCopyPath_Click`; menu `Clipboard.SetText` calls | Clipboard adapter, often UI-thread/permission dependent. Copying text/reference data is distinct from copying real files |
| Native properties | `IShellPropertiesProvider.ShowPropertiesDialog` / `ShellPropertiesProvider` | Windows implementation takes HWND; keep handles inside that adapter, offer capability/fallback on other platforms |
| Native icons | `IShellIconProvider.GetFileIcon`, `GetFolderIcon`, `GetIconByExtension`, `IconToBitmap` / `ShellIconProvider` | Existing `Icon`/`Bitmap` contract is Windows-oriented; portable consumer gets image descriptors or encoded content, not GDI objects |
| Native thumbnails | `IShellThumbnailProvider.GetThumbnail` / `ShellThumbnailProvider` | Windows COM/image handles stay in adapter; non-Windows implementation or generic fallback |
| Native context menus | `IShellContextMenuProvider.ShowContextMenu` | **Interface only; no implementation/consumer found.** Separate native file-manager menu support from existing custom WinForms menus |
| Real file operations | `IShellFileOperations.Copy`, `Move`, `Delete`, `Rename` | **Interface only; no implementation/consumer found.** Future operation API needs destinations, collision choices, recycle/permanent distinction, permission failures, progress, cancellation and partial results |
| Windows styling/activation | `ModernWindowStyler.ApplyRoundedCorners`, `ApplyExplorerListStyle`; `WindowActivator.ForceToForeground`; MF `RestoreAndActivate` | Native UX adapter only; no portable engine dependency on DWM, UxTheme, user32 or kernel32 |
| Startup/composition | Host `Main`, `RunApplication` | Host chooses storage/platform implementations and engine lifetime; WinForms retains STA/UI startup. Sample-data creation is an explicit startup policy, not an unconditional engine constructor action |
| Single instance | `SingleInstanceGuard.IsFirstInstance`, `SignalExistingInstance`, `ListenForActivation`, `Dispose` | Current named mutex/event signals activation only. Preserve single-writer behavior; alternate clients must not open independent stale writable managers against the same store |
| Updates/shipping | MF `CheckForUpdates`; Host startup timer; installer and release scripts | Windows updater/installer remain host-specific. A Mac or browser host needs its own distribution policy; do not change the current release feed during extraction |
| Explorer shell extension DLLs | Roadmap only; existing Shell assembly is a wrapper library, not a registered Explorer extension | Thin Windows integration forwards validated requests to the owning application, rather than independently writing its database. Payload-bearing IPC is new work |
| Browser extension | Roadmap only | Requires a deliberately scoped bridge, request validation and user consent; a website cannot freely call the local DLL or filesystem |
| Nest Eggs / online sharing | Roadmap only; no user import API found | Define content, IDs, schema versions, conflicts, resource mapping, recipient access and trust. Do not share raw license/UI/browser-profile data as a Nest Egg |
| Minimal plugin trial | Roadmap only; possible Copy To example | Small versioned capability surface for reference queries and permitted operations; do not expose mutable model lists or repositories. Decide plugin trust/isolation before loading third-party code |

### Existing communication mechanisms

| Existing mechanism | Where | What it means for extraction |
|---|---|---|
| Awaited method calls, no Core change events | PM and Core services | Calling UI generally refreshes itself. A shared engine needs an explicit notification contract before multiple views/plugins observe it |
| `OpenRequested`, `PropertiesRequested` | `FilePreviewPanel` -> MF handlers | Keep as view intent events; adapt to typed commands/platform requests |
| `OpenExternalRequested` | `WebResourcePreviewPanel` -> MF handler | Keep browser-control event in presentation; validate/normalize target before launch |
| `Action<SearchResult>` | `SearchForm` -> MF `OnSearchResultActivated` | Navigation callback stays presentation-level; use stable IDs, not a control instance |
| `Func<CancellationToken, T>` | `ProgressDialog.RunAsync<T>` | Existing cancellation bridge is UI-specific; expose engine work independently of modal dialog lifetime |
| Timer/async callbacks | MF availability timer, preview initialization, thumbnail tasks | Replace engine policy's dependence on WinForms timers; keep control updates on the owning UI thread |
| Activation callback with `BeginInvoke` | `SingleInstanceGuard` -> Host -> MF | Platform callback marshals into presentation; not an engine synchronization or multi-client command protocol |

### Proposed command, query, and notification contracts

Names here are illustrative, not committed class/interface names. Prefer ordinary C# methods, tasks, and a small notification surface initially; no event bus, mediator package, or distributed architecture is required for this extraction.

| Mechanism | Producer -> consumer | Minimum useful content | Required semantics |
|---|---|---|---|
| Typed command + `Task<CommandResult>` | UX/plugin adapter -> application | Target IDs, input values, optional expected revision; result includes affected IDs or error code/details | Revalidate under coordinated mutation; caller awaits success/failure. Never put a dialog callback inside domain logic |
| Read/query snapshot | Application -> UX/search/plugin | Stable IDs/types, parent/order, display data, revision; read-only nested data | `IReadOnlyList<Project>` alone is insufficient: current objects/child lists remain mutable. Do not allow clients to bypass command rules |
| `WorkspaceChanged` | Application -> subscribed views/services | Revision, operation ID, affected IDs and change kinds; reset marker when appropriate | Publish after successful logical commit, in mutation order. Subscriber failure must not undo a committed command or make it appear failed |
| `ResourceAvailabilityChanged` | Availability monitor -> views | Resource ID, checked location/version, status/reason, checked time | Ignore obsolete results after relink/delete; avoid serializing UI text; explicit retry policy |
| `LicenseStateChanged` | License/application service -> views | State and usage/limits; no raw key required | Refresh on activation/deactivation and usage changes; UI availability hints never replace command-time enforcement |
| `IProgress<OperationProgress>` | Long operation -> initiating client | Operation ID, phase, completed/total when known, current item if appropriate | Progress is not completion. Marshal in client; throttle updates; redact sensitive data in diagnostics |
| Cancellation | Client/host lifetime -> operation | `CancellationToken` | Check between work units; distinguish cancelled, failed and partial completion. Cancellation after commit does not imply rollback |
| Capability query | Host/adapters -> presentation/application | Supported actions and reason unavailable | Hide/disable unsupported commands; execution still returns `Unsupported` when needed. Do not promise Explorer features on every OS |
| External request/IPC (future) | Shell/browser adapter -> owning host | Versioned request, allowlisted action, resource data, request ID | Validate sender/payload/size/consent; deduplicate retries as needed; route through same command rules. Activation ping is not enough |

Avoid separate public repository-level success/failure events duplicating command results. Domain notifications describe committed state; failures normally return to the initiating client and diagnostics. Local C# events are not a network synchronization protocol. If online multi-client updates are later added, define authentication, authorization, version/conflict handling and reconnect/resynchronization separately.

### Correctness and portability conditions to settle before exposing the engine

1. **State ownership and durability.** PM currently changes mutable in-memory objects before awaiting persistence. A failed save can leave memory ahead of storage. Serialize commands or otherwise coordinate writes, and define staging/rollback/reload behavior. Publish snapshots/notifications only for committed state. UI-thread execution alone does not prevent interleaving across awaits.
2. **Logical transactions.** SQLite repository methods are individually transactional, but project/collection conversion makes multiple repository calls. Define an atomic multi-project operation before advertising one successful conversion event. This is a design requirement to test, not a fix implemented here.
3. **Multiple clients.** Keep a single owner of a writable workspace during the first extraction. Multiple views should share that owner, not construct separate managers. A second installed UX, shell extension or browser companion must respect ownership; future online concurrent writers need a separate concurrency model.
4. **Thread and subscription lifetime.** Engine notifications must not assume a WinForms synchronization context. Each UX dispatches and unsubscribes on close. Do not call arbitrary subscriber code while holding a mutation lock. Define initial snapshot/subscription ordering and revision handling so updates are not missed during startup.
5. **Availability policy.** Preserve current intent: network/removable references may retry every 20 seconds, web checks are on demand/first display, local missing files need relinking. Inconclusive web results are not confirmed broken links. Bound in-flight work and discard stale checks after edits.
6. **Paths and permissions.** A Windows drive path in a shared nest is not automatically usable on a Mac or server. Separate stored resource identity from host-specific resolution; account for case sensitivity, separators, UNC/mounts, symlinks, sandbox grants and missing resources. Do not rewrite paths blindly when importing.
7. **Portable contracts.** Core targets `net10.0`; Shell also targets `net10.0` but uses Windows P/Invoke and `System.Drawing.Common`. The latter is not a portable implementation. WinForms targets `net10.0-windows`. Framework names alone do not demonstrate runtime portability; test engine/storage on intended OS targets.
8. **Browser boundaries.** A Blazor Server host sees server files, not automatically the user's files. An embedded desktop browser and a website have different capabilities. Decide desktop-first versus browser-first before choosing a host/bridge. WebView2, native HWNDs and Windows clipboard rules stay outside the engine.
9. **Real file operations.** Keep Remove Reference separate from Delete File. Specify overwrite/conflict choices and partial failure reporting before Copy To or copy/paste. Do not let imported packages, previews, or external requests silently execute files or destructive operations.
10. **Export consistency.** Current personal-data export copies known files into a ZIP; do not assume that copying a live WAL-mode SQLite main file alone constitutes a consistent backup. Define a snapshot/backup strategy for future backup plugins and sharing. Keep license data out of shared packages by default.
11. **Errors and localization.** Use stable error/status codes with useful structured details; views choose wording and localization. Retain exceptions for unexpected faults, and do not silently translate storage failure into success or empty state. No changes to the current perpetual-license policy are implied.

### Incremental extraction and acceptance gates

| Pass | Work to agree before implementation | Evidence required before moving on |
|---|---|---|
| 0. Baseline | Confirm checkout/local edits; checkpoint intended work; capture current build/test and essential V1 workflows | Reproducible baseline; committed V1 history preserved; no release-feed changes |
| 1. First engine slice | Project/reference query plus one add/edit/remove workflow; validation and limits behind application boundary | Headless tests; WinForms performs same workflow; no forms, HWNDs or mutable domain objects in public client contract |
| 2. Reliable change contract | Define snapshots, revisions, failure semantics and mutation coordination | Failure-injected persistence tests, ordered post-commit notifications, subscription cleanup and concurrent-command tests |
| 3. Remaining nest commands | Reorder, conversions, metadata, search, availability | Existing regression behavior retained; multi-project atomicity and stale availability results covered |
| 4. Platform boundaries | Directory reading, launches, thumbnails, clipboard, previews and lifecycle | WinForms still works on Windows; capability fallbacks tested with fakes; portable engine tests run separately from Windows integration tests |
| 5. Modern UX pilot | Choose host; reproduce one complete real user journey against the engine | Both clients work against equivalent isolated test data without duplicating rules; compare usability with prospective users |
| 6. V2 integrations | Scope Nest Eggs/online operations, shell IPC and optional Copy To trial | Same command validation and ownership rules; explicit trust/permission model; no independent stale writers |

Current test files cover PM operations, SQLite persistence, migration, search, availability, URL normalization, preview classification, image navigation and personal-data export. The test project references Core and Shell, not WinForms. This planning pass did not execute tests or establish UI/runtime portability. Add targeted coverage for license enforcement through commands, save failure, conversion transaction failure, notification timing/lifetime, cancellation, stale asynchronous results and host capabilities. For Windows-specific behavior, retain explicit manual/UI integration checks rather than claiming a headless suite covers it.

### Decisions still open

- Modern Windows desktop first, or browser access as a requirement for the next release? What is the earliest required macOS workflow?
- What are the first UX journeys to improve, and how will prospective users judge the improvement?
- Where does a writable engine live: inside the desktop host initially, or in a separately managed local service? Prefer in-process initially unless a concrete integration requires otherwise.
- Nest Egg contents, reference resolution, recipient permissions, schema/versioning and relationship to `.peproj`.
- Which Explorer operations are essential, and which modify real files versus nest references?
- Scope and trust model of the tentative Copy To plugin; broad plugin support remains V3.

This plan deliberately preserves those choices rather than selecting a framework or building speculative interfaces now.
