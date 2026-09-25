# Sharing phase 1 — two computers, one project

This is the first slice of Version 2. The goal is something you can run yourself: send one project from one computer and import it on another, then look at what survived. That use is what decides the later online design. It is not accounts, live sync, or a Blazor host.

`File ▸ Export All My Data...` is unchanged. That zip is a personal archive. A Nest Egg is one project, sent on purpose.

## What you do

1. On SQL Server, run [`src/ProjectNest.Server/Sql/001_CreateSharingDatabase.sql`](../src/ProjectNest.Server/Sql/001_CreateSharingDatabase.sql) once in SSMS. That creates database `ProjectNestSharing`, the three tables, and the stored procedures. Local `dotnet run` is Development, so it reads `ConnectionStrings:ControlPlane` from `src/ProjectNest.Server/appsettings.Development.json` (database `ProjectNestSharing` on `MIGHTYK10\SQLEXPRESS`). `DefaultConnection` in that file points at `db_acdaa1_conproddb` and is not the sharing store. `Sharing:ConnectionString` in `appsettings.json` stays empty. To point a different machine at SQL Server, set `Sharing:ConnectionString` or the environment variable `Sharing__ConnectionString`; that value wins over ControlPlane. A set `Sharing:DatabasePath` stays on SQLite, which is how the automated tests run.

2. Start the sharing server on the computer that other machines can reach:

   ```bash
   dotnet run --project src/ProjectNest.Server
   ```

   The server listens on `http://0.0.0.0:5088`. A browser on that machine can open `http://localhost:5088` and should see a one-line confirmation. The startup log says it is using SQL Server via `ConnectionStrings:ControlPlane`. If that string and `Sharing:ConnectionString` are both empty, it falls back to a local SQLite file instead. Each contact (`/` and `/api/health`) and each share request is written to that same console, including the path, status code, and the caller's address.

3. If the other computer is on the same network, allow the port through Windows Firewall on the server machine:

   ```powershell
   netsh advfirewall firewall add rule name="Project Nest Sharing" dir=in action=allow protocol=TCP localport=5088
   ```

   Find the server's LAN address with `ipconfig` (the IPv4 address on the Wi-Fi or Ethernet adapter, for example `192.168.1.20`).

4. On computer A, select a project and choose **File ▸ Share Project…**. Set the server to `http://192.168.1.20:5088` (or `http://localhost:5088` if the app and the server are on the same machine). Share. Copy the code (`ABCD-EFGH`).

5. On computer B, choose **File ▸ Receive Shared Project…**, use the same server address, enter the code, Preview, then Import.

6. On computer A, **Refresh activity**. You should see Created, Previewed, Fetched, and Imported, with each computer's name.

7. Look at the imported project on B. Web addresses should still open. Folder and file paths are the paths from A. If those disks are not on B, the rows show as unavailable. That is the result this slice is for.

The server address and the computer name are remembered in `appsettings.json` on each machine.

A code lasts 7 days. **Revoke code** on the share dialog makes the next fetch fail. Anyone who has the code can fetch the project until then. Do not share a project that has secrets in its names, descriptions, or URLs. The server has no accounts.

## Server database

SQL Server database `ProjectNestSharing`, created by `src/ProjectNest.Server/Sql/001_CreateSharingDatabase.sql`. It is separate from each computer's `%APPDATA%\ProjectExplorer\projects.db` and from `db_acdaa1_conproddb`. The sharing server does not create this database. You run the script once in SSMS. Local Development then uses `ConnectionStrings:ControlPlane` for the instance. Before it connects, the server writes `Sharing:Database` (`ProjectNestSharing`) in as the SQL Server catalog, including when the connection string names a different database on that instance. `Sharing:LifetimeDays` defaults to 7 and is capped at 30.

The script creates three tables and these procedures: `dbo.usp_Share_CodeExists`, `dbo.usp_Share_Create`, `dbo.usp_Share_GetByCode`, `dbo.usp_ShareEvent_Insert`, `dbo.usp_Share_RecordFetch`, `dbo.usp_Share_RecordImport`, `dbo.usp_Share_Revoke`, and `dbo.usp_ShareEvent_List`. Re-running the script updates the procedures (`CREATE OR ALTER`) and leaves existing tables in place.

Times are `datetime2` stored as UTC. Ids are `uniqueidentifier`. The egg JSON is `nvarchar(max)`.

### NestEggs

One row per published project. The payload is the Nest Egg JSON. The row is not updated after insert.

| Column | Role |
|---|---|
| `Id` | Server id of this stored egg (`uniqueidentifier`, primary key) |
| `SchemaVersion` | Egg format. Phase 1 accepts `1` only |
| `CreatedUtc` | When the server stored it (`datetime2`, UTC) |
| `ExpiresUtc` | Same instant as the share's expiry |
| `SenderLabel` | The computer name typed in Share Project |
| `ProjectName` | Copied from the egg so a listing does not have to parse JSON |
| `SourceProjectId` | The project id on the sending computer |
| `PayloadJson` | The egg document |
| `PayloadSha256` | SHA-256 of `PayloadJson` (hex). Fetch sends it back as `X-Payload-Sha256` |
| `ByteLength` | UTF-8 size of `PayloadJson`. Rejected above 2,000,000 bytes |

### Shares

The claim ticket for one egg. Phase 1 creates one share per egg.

| Column | Role |
|---|---|
| `Id` | `uniqueidentifier`, primary key |
| `EggId` | References `NestEggs.Id` |
| `Code` | 8 characters from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, stored without a hyphen, unique |
| `CreatedUtc` | When the code was issued |
| `ExpiresUtc` | After this instant, fetch and preview return "expired" |
| `RevokedUtc` | Set by Revoke. Null while the code still works |
| `FetchCount` | Times the full egg was downloaded |
| `ImportCount` | Times a receiver reported a successful import |

The code is shown to people as `ABCD-EFGH`. The server accepts it with or without the hyphen.

### ShareEvents

Append-only. This is the log you read with Refresh activity.

| Column | Role |
|---|---|
| `Id` | `uniqueidentifier`, primary key |
| `ShareId` | The share this event belongs to |
| `EggId` | The egg |
| `EventType` | `Created`, `Previewed`, `Fetched`, `Imported`, `Revoked`, or `Rejected` |
| `OccurredUtc` | When the server wrote the row |
| `MachineLabel` | Computer name, when the caller sent one. Truncated at 80 characters |
| `Detail` | Short note (project name on create, "expired"/"revoked" on reject, the new project id on import). Truncated at 500 characters |

`IX_ShareEvents_ShareId_OccurredUtc` supports the activity list.

What is deliberately not in this database: user accounts, devices, license keys, file bytes, UI layout, and the receiver's copy of the project. The receiver's copy lives only in that computer's `projects.db`.

## The Nest Egg

Schema 1, kind `project-nest-egg`. Built by `NestEggCodec` in `ProjectExplorer.Core`. It is an explicit document, not a dump of the live `Project` object.

```json
{
  "schemaVersion": 1,
  "kind": "project-nest-egg",
  "createdUtc": "2026-09-21T17:00:00.0000000Z",
  "source": { "machineLabel": "OFFICE", "appVersion": "1.0.9" },
  "project": {
    "sourceId": "original-project-guid",
    "name": "Client Site",
    "description": null,
    "color": null,
    "iconKey": null,
    "createdUtc": "2026-09-01T12:00:00.0000000Z",
    "modifiedUtc": "2026-09-20T12:00:00.0000000Z",
    "nodes": [
      {
        "sourceId": "original-child-guid",
        "parentSourceId": "original-project-guid-or-collection-guid",
        "childType": "folderReference",
        "sortOrder": 0,
        "displayName": "Logo",
        "name": null,
        "description": null,
        "color": null,
        "realPath": "D:\\Clients\\Site\\assets",
        "url": null,
        "filePath": null,
        "openExternalOnly": false,
        "metadata": { "tag": "keep-me" }
      }
    ]
  }
}
```

`childType` is `collection`, `folderReference`, `webResource`, or `fileReference`. Nodes are a flat list. A node's parent is the project or a collection in the same list. The server rejects a missing parent, a cycle, a duplicate id, an unknown type, and payloads over the size limit (5,000 nodes, 2 MB).

Keys that start with `shared.` are bookkeeping from an earlier import. They are stripped before the egg is built, so a project that was itself received can be sent onward without dragging the previous share's ids along. Other metadata (for example "stop auto-retry") is kept.

Import (`NestEggImporter`) always creates a **new** local project with **new** ids. Paths and URLs are copied unchanged. Nothing already on the receiving computer is replaced. If the name is already taken, the new project is named `Name (shared)`, then `Name (shared 2)`. A second receive of the same code is another copy. There is no merge in this slice.

The free tier is enforced on import: 5 projects and 50 folder/file/web references, same numbers as creating them by hand. Collections do not count. A licensed install is not limited.

Provenance is stored on the local project so it is still there after a restart:

| Where | Key | Value |
|---|---|---|
| `Projects.MetadataJson` | `shared.sourceProjectId` | Project id on the sender |
| | `shared.shareCode` | The code that was entered |
| | `shared.senderLabel` | Sender computer name |
| | `shared.importedUtc` | When this computer imported it |
| Each child `MetadataJson` | `shared.sourceNodeId` | That item's id on the sender |

`Projects.MetadataJson` is a new nullable column on the local database, added the same way as earlier column additions. The sharing server does not read it.

## Data flows

```
Computer A                          Sharing server                         Computer B
    |                                      |                                    |
    |  POST /api/shares                    |                                    |
    |  { machineLabel, egg }               |                                    |
    |------------------------------------->|                                    |
    |                                      | insert NestEggs                    |
    |                                      | insert Shares                      |
    |                                      | insert ShareEvents Created         |
    |  { code, expiresUtc, projectName }   |                                    |
    |<-------------------------------------|                                    |
    |                                      |                                    |
    |                                      |  GET /api/shares/{code}?machine=   |
    |                                      |<-----------------------------------|
    |                                      | ShareEvents Previewed              |
    |                                      |  preview counts and paths          |
    |                                      |----------------------------------->|
    |                                      |                                    |
    |                                      |  GET /api/shares/{code}/egg        |
    |                                      |<-----------------------------------|
    |                                      | FetchCount++, ShareEvents Fetched  |
    |                                      |  egg JSON + X-Payload-Sha256       |
    |                                      |----------------------------------->|
    |                                      |                                    |
    |                                      |     B writes a new project         |
    |                                      |     into its own projects.db       |
    |                                      |                                    |
    |                                      |  POST /api/shares/{code}/events    |
    |                                      |  { eventType: Imported, ... }      |
    |                                      |<-----------------------------------|
    |                                      | ImportCount++, ShareEvents Imported|
    |                                      |                                    |
    |  GET /api/shares/{code}/events       |                                    |
    |------------------------------------->|                                    |
    |  Created, Previewed, Fetched,        |                                    |
    |  Imported                            |                                    |
    |<-------------------------------------|                                    |
```

| Call | Who | Result |
|---|---|---|
| `GET /api/health` | Either app, Test connection | `{ service, phase: 1 }` |
| `POST /api/shares` | Sender | `400` with `{ error }` when the egg is invalid. Otherwise the code |
| `GET /api/shares/{code}?machine=` | Receiver, Preview | Counts plus up to 20 folder paths, file paths, and URLs. `404` unknown, `400` malformed code, `410` expired or revoked |
| `GET /api/shares/{code}/egg?machine=` | Receiver, during Preview (so Import does not depend on a second guess) | The stored JSON. Header `X-Payload-Sha256`. The receiver checks it |
| `POST /api/shares/{code}/events` | Receiver, after the local save | Body `{ eventType: "Imported", machineLabel, detail }`. Any other event type is `400`. The server writes Created, Previewed, Fetched, Revoked, and Rejected itself |
| `GET /api/shares/{code}/events` | Sender, Refresh activity | The log, including after a revoke, so you can still see what happened |
| `DELETE /api/shares/{code}?machine=` | Sender, Revoke code | Sets `RevokedUtc` |

`Rejected` is written when someone asks for a code that is already expired or revoked.

The local nest is still single-writer. The server never opens `projects.db`. Computer B's import goes through `ProjectManager.ImportSharedProjectAsync`, which checks the license and then saves that one project.

## What this slice is here to teach

After a real send between two machines, the useful notes are:

- Which paths were meaningful on the other computer, and which only became unavailable rows.
- Whether web resources were the part that actually traveled well.
- Whether names, nesting, descriptions, colors, and "always open in an external browser" arrived intact.
- Whether an 8-character code is something you will actually type.
- Whether seeing Previewed / Fetched / Imported is enough to trust that the other computer got it.
- Whether "always a new copy" is what you want the second time you send the same project, or whether you wanted an update.

Those answers are the input to accounts, merge, and any later Blazor or MAUI host. They are not settled here.

## Publishing to project-nest.com

The public address is `https://project-nest.com`. SmarterASP.net hosts that site. The sharing server is not inside the desktop installer. GitHub Actions workflow **Deploy sharing server** (`.github/workflows/deploy-sharing.yml`) publishes `ProjectNest.Server` there when you run it from the Actions tab.

Enable Web Deploy first: Control Panel → Websites → the site → Manage Website → VS Webdeploy. Copy the values from that page into these repository secrets (Settings → Secrets and variables → Actions):

| Secret | Value |
|---|---|
| `SMARTERASP_SITE` | Site/Application name, for example `username-001-site1` |
| `SMARTERASP_SERVICE_URL` | Service URL, for example `https://winxxxx.site4now.net:8172/MsDeploy.axd?site=username-001-site1` |
| `SMARTERASP_USERNAME` | Web Deploy user, for example `username-001` |
| `SMARTERASP_PASSWORD` | Web Deploy password |
| `SMARTERASP_CONNECTION_STRING` | SQL connection from Database Manager → MSSQL Manager. End it with `Encrypt=yes;TrustServerCertificate=true` when the host certificate is self-signed |

The workflow publishes a self-contained 64-bit build, because the SmarterASP server may not have the .NET 10 runtime, and writes the connection string into `web.config` as `Sharing__ConnectionString`. Production does not read `appsettings.Development.json`. Without that secret the site would use a SQLite file on the web server. The workflow fails before publish if any of the five secrets is empty. It does not print the password. Web Deploy follows SmarterASP's own flags: allow the host certificate, take the site offline during the sync, and do not delete files already on the site.

On the SmarterASP SQL database, run `src/ProjectNest.Server/Sql/001_CreateSharingDatabase.sql`. Shared SQL often cannot `CREATE DATABASE`. Create the database in the panel, name it `ProjectNestSharing` when the panel allows that name, and run the rest of the script inside it. If the panel assigns a different name, set `Sharing__Database` to that name (the workflow currently sets it to `ProjectNestSharing`).

Point `project-nest.com` at this SmarterASP site and bind a certificate for that name in the control panel. Until HTTPS for that name succeeds, the desktop app stays on the local server (`http://localhost:5088` or the LAN address).

## Left out on purpose

- Accounts, passwords, and "this nest belongs to this person."
- Uploading file or folder contents.
- Replacing or merging a project that is already on the receiver.
- A second writer of `projects.db`, including the sharing server.
- Blazor, MAUI, shell extensions, and browser extensions.
