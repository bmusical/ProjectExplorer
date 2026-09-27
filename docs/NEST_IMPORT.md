# Nest Import

This spec defines **Nest Import**: a JSON file another program can write, and **File ▸ Nest Import…** to add that file to the local tree. No share code.

This version of the file is an outline. The egg is a project made entirely of collections. It has no folder references, no file references, and no web resources. Those come later, hung on the branches this profile creates. A video's chapters are the first use. The same import is a general tree of collections.

This is not a new format. It is schema 1, kind `project-nest-egg`, the same document [`SHARING_PHASE1.md`](SHARING_PHASE1.md) already imports. A second kind or schema version would be rejected.

**File ▸ Nest Import…** reads the file and creates a new project. When `source.appVersion` is `outline-1`, import checks the rules in this document and refuses the file if any node is not an empty collection. Any other valid Nest Egg — including a later file that has hung resources on these branches — uses the same command and skips those extra checks.

One file is one project. Import always creates a new project. Writing the file again and importing it again does not update the project already on disk.

## What the tree means

| Outline idea | Egg field | What Project Nest Explorer shows |
|---|---|---|
| The video | `project` | One project. The tree label is `project.name`. |
| A chapter, section, or beat | one `nodes[]` entry with `childType` `"collection"` | A collection. The tree label is `name`. The tooltip is `description` when that is set, otherwise `name`. |
| "This branch is inside that one" | `parentSourceId` | Nesting. There is no nested JSON. The node list is flat. |
| Playback order among siblings | `sortOrder` | Children of a collection are shown in `sortOrder` order. |

A **chapter** is a collection whose parent is the project. A **section** is a collection whose parent is a chapter. A **beat** is a collection whose parent is a chapter or a section, and which has no child collections.

The later resource pass attaches folder, file, and web references to **leaf** collections: a beat, or a chapter or section that has no child collections. It does not insert a reference between a chapter and its sections. This profile includes none of those references, so every collection is still an empty branch waiting for that pass.

Collections do not count toward the free tier's 50 folder/file/web references. The outline does use one of the 5 free projects.

## File

Write UTF-8 JSON. The conventional name is `<video-slug>.nestegg.json`. Indentation does not matter. Property names are camelCase.

The worked example is [`examples/video-chapter-outline.nestegg.json`](examples/video-chapter-outline.nestegg.json).

## Envelope

```json
{
  "schemaVersion": 1,
  "kind": "project-nest-egg",
  "createdUtc": "2026-09-27T19:00:00.0000000Z",
  "source": {
    "machineLabel": "outliner",
    "appVersion": "outline-1"
  },
  "project": { }
}
```

| Field | Rule |
|---|---|
| `schemaVersion` | `1`. Any other value is rejected. |
| `kind` | `project-nest-egg`. Any other value is rejected. |
| `createdUtc` | When this file was written, UTC, ISO-8601, with a `Z`. |
| `source.machineLabel` | Required. 1–80 characters after trimming. The video tool uses `outliner`. |
| `source.appVersion` | `outline-1` for this version. That marks the file as collections only. It is not stored on the imported project. |

## Project

| Field | Rule |
|---|---|
| `sourceId` | A new GUID for this file. Not `00000000-0000-0000-0000-000000000000`. |
| `name` | The project name. Required after trimming. 1–200 characters. This is the label in the tree. For a video, use the video title. |
| `description` | Optional one-paragraph note (logline, who the video is for). Omit or `null` when there is nothing to say. Max 4,000 characters. |
| `color` | `null` or omit. |
| `iconKey` | `null` or omit. |
| `createdUtc`, `modifiedUtc` | UTC timestamps for this file. Use the same instant as the envelope `createdUtc` unless you have a real earlier draft time. |
| `nodes` | The flat collection list. May be empty. A finished outline has at least one collection. |

## Collections

Every element of `nodes` is one collection. The list is a preorder walk: a collection appears before its children, and siblings appear in playback order.

```json
{
  "sourceId": "22222222-2222-4222-8222-222222222201",
  "parentSourceId": "11111111-1111-4111-8111-111111111111",
  "childType": "collection",
  "sortOrder": 0,
  "displayName": null,
  "name": "1. Cold open",
  "description": "State the problem in one sentence, then the payoff.",
  "color": null,
  "realPath": null,
  "url": null,
  "filePath": null,
  "openExternalOnly": false,
  "metadata": {
    "outline.profile": "collections-only",
    "outline.role": "chapter",
    "outline.key": "cold-open"
  }
}
```

| Field | Rule |
|---|---|
| `sourceId` | A new GUID, unique in this file, different from `project.sourceId`. |
| `parentSourceId` | The project's `sourceId` for a top-level collection. Otherwise the parent collection's `sourceId`. |
| `childType` | `collection` only. `folderReference`, `fileReference`, and `webResource` are valid in a full Nest Egg and are forbidden in this profile. |
| `sortOrder` | `0`, `1`, `2`, … among **siblings**. Restart at `0` for each parent. Unique within the parent. The first sibling is the first thing the viewer hits. |
| `name` | The heading shown in the tree. Required after trimming. 1–200 characters. One line. |
| `displayName` | `null` or omit. The tree uses `name` for collections. |
| `description` | Optional note for this branch. Omit or `null` when empty. Max 4,000 characters. Shown as the tooltip. |
| `color` | `null` or omit. |
| `realPath`, `url`, `filePath` | `null` or omit. |
| `openExternalOnly` | `false` or omit. |
| `metadata` | Optional. A general outline can omit it. Do not use keys that start with `shared.` |

Do not nest a `nodes` array inside a collection. Parent and child are linked only by `parentSourceId`.

Nesting depth is not capped, beyond the 5,000-node limit. A video outline should stop at three levels under the project. The importer does not require that.

### Optional metadata

Import copies metadata onto the collection. Omit the whole object when there is nothing to record.

| Key | When to write it |
|---|---|
| `outline.profile` | `collections-only`, if you want the branch marked after import. |
| `outline.role` | `chapter`, `section`, or `beat`. The video tool writes this. A general outline leaves it off. |
| `outline.key` | A stable slug so a later pass can find the branch after ids are replaced. |

When `outline.role` is set, these rules apply to the nodes that have it:

- `chapter` — parent is the project.
- `section` — parent role is `chapter`.
- `beat` — parent role is `chapter` or `section`, and this collection has no children.
- A chapter's children that have roles are all sections, or all beats. Do not mix them.
- A section's children that have roles are beats.

When `outline.key` is set:

- 1–64 characters. Lowercase ASCII letters, digits, and single hyphens: `cold-open`.
- Unique in the file. Does not start or end with a hyphen.
- Mint new `sourceId` values every time you write a file. Keep `outline.key` stable when you regenerate the same outline. Import replaces every GUID. The imported collection's `shared.sourceNodeId` is the file's `sourceId`. `outline.key` is the handle that survives a rewrite.

## Shape the tree for the resource pass

The outliner decides the branches. It does not fill them.

- A leaf collection is a resource attachment point. A later egg may add `folderReference`, `fileReference`, and `webResource` nodes whose `parentSourceId` is that leaf's `sourceId`.
- An interior collection (a chapter that has sections, a section that has beats) stays a heading. Do not plan to hang references on it.
- Order the leaves in the order the video uses them. `sortOrder` is that order.
- Put the words a person needs in `name`. Put the instruction to the later resource pass in `description` when a heading is not enough ("B-roll of the sharing dialog", "the `updates.xml` URL"). Leave `description` null when the heading is enough.
- Do not encode a path, a URL, or a file name in `name` or in metadata. Those are the next profile's `realPath`, `filePath`, and `url`.

## Limits the codec already enforces

Stay inside these. Nest Import rejects an egg that breaks them.

| Limit | Value |
|---|---|
| JSON size | 2,000,000 bytes, UTF-8 |
| Nodes | 5,000 |
| `name` | 200 characters |
| `description` and other text | 4,000 characters |
| `source.machineLabel` | 80 characters |
| `source.appVersion` | 40 characters |
| Metadata entries on one node | 40 |
| Metadata key | 80 characters |
| Metadata value | 2,000 characters |

Also rejected: a missing parent, a parent that is not the project or a collection, a cycle, a duplicate `sourceId`, a node that reuses the project's id, an empty project name, an empty collection name.

## How to build the node list

```
projectId = new GUID
nodes = []

function walk(parentId, branches):
    for index, branch in enumerate(branches):   # playback order
        id = new GUID
        nodes.append(collection(
            sourceId: id,
            parentSourceId: parentId,
            sortOrder: index,
            name: branch.heading,
            description: branch.note or null,
            role: branch.role,
            key: branch.key))
        walk(id, branch.children)

walk(projectId, chapters)
```

`chapters` is the outliner's tree. `nodes` is the flat list written to `project.nodes`.

Playback order is `sortOrder` among siblings, not the position of an object in `nodes`. Import sorts by `sortOrder`. Writing the list as a preorder walk keeps the file readable; it is not what the importer uses to order children.

## Worked tree

[`examples/video-chapter-outline.nestegg.json`](examples/video-chapter-outline.nestegg.json) is this video:

```
Cutting a release                         project
├─ 1. Cold open                           chapter   cold-open
│  ├─ The problem                         beat      cold-open-problem
│  └─ The payoff                          beat      cold-open-payoff
├─ 2. Where the nest lives                chapter   where-the-nest-lives
│  ├─ The database file                   section   database-file
│  │  └─ projects.db                      beat      projects-db
│  └─ Collections are not folders         section   collections-are-not-folders
├─ 3. Hand it to the other computer       chapter   hand-it-over
│  ├─ Start the sharing server            beat      start-the-server
│  ├─ Send the code                       beat      send-the-code
│  └─ Receive it                          beat      receive-it
└─ 4. Close                               chapter   close
```

Chapter 2 mixes nothing: its children are both sections. "Collections are not folders" is a leaf section, so the resource pass may hang references directly on it. "The database file" is an interior section because it has a beat. Chapter 4 is a leaf chapter.

## Import

**File ▸ Nest Import…** opens a `.nestegg.json` (or any `.json`) file and adds it as a new project in the local tree. Nothing already in the nest is replaced. There is no share code.

`NestEggFile.Read` reads the UTF-8 file. `NestEggImporter.MaterializeFromFile` builds the project. `ProjectManager.ImportSharedProjectAsync` applies the free-tier check and saves that one project.

When `source.appVersion` is `outline-1`, import runs `NestEggOutline.Validate` before saving. A folder, file, or web node, a path or URL on a collection, a broken `outline.role`, or a broken `sortOrder` refuses the whole file. The message is the reason. No partial tree is written. Missing `outline.role` and `outline.key` are allowed.

A file whose `appVersion` is anything else is a normal Nest Egg. Import keeps its folders, files, and URLs and does not apply the collections-only rules. That is the door a later Nest Import will use when a file hangs resources on these branches.

If a project named `Cutting a release` already exists, the new one is named `Cutting a release (2)`, then `Cutting a release (3)`. Importing the same file again is another copy.

Ids in the file are not the ids stored in `projects.db`. Each imported collection keeps whatever `outline.*` keys the file included, and gains `shared.sourceNodeId` (the file's `sourceId`). The project gains:

| Key | Value |
|---|---|
| `shared.sourceProjectId` | `project.sourceId` from the file |
| `shared.senderLabel` | `source.machineLabel` |
| `shared.importedUtc` | When this computer imported it |
| `shared.importFile` | The file name only, not the directory |

Nest Import does not write `shared.shareCode`.

This is not a restore of **File ▸ Export All My Data…**. That zip is still one-way.

## Out of this version

- Folder, file, and web references. A later Nest Import can add them to this same JSON. Those files are not `outline-1`.
- File bytes, scripts stored as documents, and license or window data.
- Updating or merging a project that is already imported.
- A plugin. The other program writes the file. Project Nest Explorer imports it.
