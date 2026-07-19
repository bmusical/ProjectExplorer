using ProjectExplorer.Core.Interfaces;

namespace ProjectExplorer.Core.Services;

/// <summary>
/// Resolves which IProjectRepository the app should use for this run, performing a one-time
/// migration from the legacy JSON store (projects.json) to the new SQLite store (projects.db)
/// the first time it's needed.
///
/// Safety property that matters most here: a user must never see their project tree appear to
/// vanish. If migration fails for any reason (corrupt JSON, disk error, etc.), projects.json is
/// left untouched and this run falls back to JsonProjectRepository — the app behaves exactly as
/// it did before this feature shipped, and migration is retried on the next launch. projects.json
/// is only ever renamed to projects.json.migrated after a verified-successful write into SQLite.
/// projects.json.bak (the existing per-save backup) is never touched by migration either way.
///
/// Migration writes into a temp file and only becomes visible at the real "projects.db" path via
/// a single atomic File.Move once it's fully written and checkpointed (see
/// SqliteProjectRepository.Checkpoint). This is deliberate: an earlier version of this class
/// wrote directly to "projects.db", which meant a crash, force-kill, or power loss partway
/// through migration could leave a partially-written file sitting at that exact path — and the
/// "if projects.db already exists, treat it as already migrated" check below would then trust
/// that partial file forever, permanently orphaning the untouched-but-no-longer-consulted
/// projects.json. Since nothing is ever written to the real path until migration fully succeeds,
/// that failure mode can no longer happen: an interrupted run leaves no trace at "projects.db" at
/// all, so a later launch correctly sees projects.json still there and retries cleanly.
/// </summary>
public static class ProjectStoreMigrator
{
    public static IProjectRepository ResolveRepository(string storageDir)
    {
        Directory.CreateDirectory(storageDir);
        var dbPath = Path.Combine(storageDir, "projects.db");
        var jsonPath = Path.Combine(storageDir, "projects.json");

        if (File.Exists(dbPath))
            return new SqliteProjectRepository(storageDir); // already migrated (or a fresh SQLite-only install)

        if (!File.Exists(jsonPath))
            return new SqliteProjectRepository(storageDir); // fresh install, nothing to migrate

        var tempFileName = $"projects.db.migrating-{Guid.NewGuid():N}";
        var tempDbPath = Path.Combine(storageDir, tempFileName);
        try
        {
            var projects = new JsonProjectRepository(storageDir).LoadAllAsync().GetAwaiter().GetResult();

            var tempRepo = new SqliteProjectRepository(storageDir, tempFileName);
            tempRepo.SaveAllAsync(projects).GetAwaiter().GetResult();
            tempRepo.Checkpoint();

            // The only step that can make data appear at the real "projects.db" path — an atomic
            // rename, so that path is never observable in a partially-migrated state.
            File.Move(tempDbPath, dbPath);
            // The checkpoint above already flushed all data into the main temp file and truncated
            // these to empty, so nothing of value is in them by this point — just tidy up the
            // now-orphaned (still temp-named) side files rather than leaving them behind.
            DeleteIfExists(tempDbPath + "-wal");
            DeleteIfExists(tempDbPath + "-shm");
            File.Move(jsonPath, jsonPath + ".migrated");
            return new SqliteProjectRepository(storageDir);
        }
        catch
        {
            // Don't leave a partially-written temp db around; don't touch projects.json either,
            // so the app keeps working on the old backend exactly as before and migration is
            // retried next time.
            DeleteIfExists(tempDbPath);
            DeleteIfExists(tempDbPath + "-wal");
            DeleteIfExists(tempDbPath + "-shm");
            return new JsonProjectRepository(storageDir);
        }
    }

    private static void DeleteIfExists(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
