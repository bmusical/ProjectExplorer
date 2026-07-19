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

        try
        {
            var projects = new JsonProjectRepository(storageDir).LoadAllAsync().GetAwaiter().GetResult();

            var sqliteRepo = new SqliteProjectRepository(storageDir);
            sqliteRepo.SaveAllAsync(projects).GetAwaiter().GetResult();

            File.Move(jsonPath, jsonPath + ".migrated");
            return sqliteRepo;
        }
        catch
        {
            // Don't leave a partially-written db around to be mistaken for a completed migration
            // next launch; don't touch projects.json either, so the app keeps working on the old
            // backend exactly as before and migration is retried next time.
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* best effort */ }
            return new JsonProjectRepository(storageDir);
        }
    }
}
