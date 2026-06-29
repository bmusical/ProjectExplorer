using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectExplorer.Core.Models;

namespace ProjectExplorer.Core.Services;

/// <summary>
/// Manages free-tier limits and license key verification for Project Nest Explorer.
///
/// Free tier: up to 3 projects and 25 leaf nodes (FolderReferences + WebResources)
/// total across all projects. Collections are not counted — they are just containers.
///
/// Keys are ECDSA-signed payloads: "email|FULL|yyyy-MM-dd"
/// Verification uses the embedded public key — no network call required.
/// </summary>
public sealed class LicenseManager
{
    // ── Free-tier limits ──────────────────────────────────────────────────────
    public const int FreeProjectLimit  = 3;
    public const int FreeLeafNodeLimit = 25;

    // ── Key verification ──────────────────────────────────────────────────────
    // Replace with your real ECDSA P-256 public key (PEM) after generating your keypair.
    // In dev mode (placeholder present) keys are accepted as plain "email|FULL|date" strings.
    private const string PublicKeyPem = "DEVELOPMENT_KEY_PLACEHOLDER";

    // ── Storage ───────────────────────────────────────────────────────────────
    private readonly string _storageDir;
    private readonly string _licenseFile;

    public LicenseManager() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ProjectExplorer"))
    { }

    internal LicenseManager(string storageDir)
    {
        _storageDir  = storageDir;
        _licenseFile = Path.Combine(storageDir, "license.json");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current license state given the live project list.
    /// Call this on startup and after any project/node change.
    /// </summary>
    public LicenseInfo GetCurrentLicense(IEnumerable<Project> projects)
    {
        var stored = LoadStoredLicense();
        if (stored != null)
        {
            var (valid, email, date) = VerifyKey(stored.Key);
            if (valid)
                return new LicenseInfo { State = LicenseState.Licensed, Email = email, LicensedOn = date };

            return new LicenseInfo { State = LicenseState.Invalid };
        }

        var projectList  = projects.ToList();
        int projectCount = projectList.Count;
        int leafCount    = CountLeafNodes(projectList);

        var state = (projectCount > FreeProjectLimit || leafCount > FreeLeafNodeLimit)
            ? LicenseState.LimitReached
            : LicenseState.Free;

        return new LicenseInfo
        {
            State         = state,
            ProjectCount  = projectCount,
            LeafNodeCount = leafCount,
            ProjectLimit  = FreeProjectLimit,
            LeafNodeLimit = FreeLeafNodeLimit
        };
    }

    /// <summary>
    /// Attempts to activate with the supplied key. Returns the resulting LicenseInfo.
    /// On success the key is persisted to disk.
    /// </summary>
    public LicenseInfo Activate(string key, IEnumerable<Project> projects)
    {
        key = key.Trim();
        var (valid, email, date) = VerifyKey(key);

        if (!valid)
            return new LicenseInfo { State = LicenseState.Invalid };

        Directory.CreateDirectory(_storageDir);
        File.WriteAllText(_licenseFile,
            JsonSerializer.Serialize(new StoredLicense { Key = key }),
            Encoding.UTF8);

        return new LicenseInfo { State = LicenseState.Licensed, Email = email, LicensedOn = date };
    }

    public void Deactivate()
    {
        if (File.Exists(_licenseFile)) File.Delete(_licenseFile);
    }

    // ── Node counting ─────────────────────────────────────────────────────────

    public static int CountLeafNodes(IEnumerable<Project> projects) =>
        projects.Sum(p => CountLeavesIn(p.Children));

    private static int CountLeavesIn(IEnumerable<ProjectChild> children)
    {
        int count = 0;
        foreach (var child in children)
        {
            if (child is Collection c)
                count += CountLeavesIn(c.Children);
            else
                count++; // FolderReference or WebResource
        }
        return count;
    }

    // ── Key verification ──────────────────────────────────────────────────────

    private (bool valid, string? email, DateTime? date) VerifyKey(string licenseKey)
    {
        if (PublicKeyPem == "DEVELOPMENT_KEY_PLACEHOLDER")
            return ParsePayloadUnchecked(licenseKey);

        try
        {
            var parts = licenseKey.Split('.');
            if (parts.Length != 2) return (false, null, null);

            var payloadBytes = Base64UrlDecode(parts[0]);
            var signature    = Base64UrlDecode(parts[1]);
            var payload      = Encoding.UTF8.GetString(payloadBytes);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(PublicKeyPem);

            return ecdsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256)
                ? ParsePayload(payload)
                : (false, null, null);
        }
        catch { return (false, null, null); }
    }

    private static (bool, string?, DateTime?) ParsePayload(string payload)
    {
        var parts = payload.Split('|');
        if (parts.Length != 3 || parts[1] != "FULL") return (false, null, null);
        if (!DateTime.TryParse(parts[2], out var date)) return (false, null, null);
        return (true, parts[0], date);
    }

    // Dev mode: accept "email|FULL|date" directly
    private static (bool, string?, DateTime?) ParsePayloadUnchecked(string key)
    {
        var parts = key.Split('|');
        if (parts.Length == 3 && parts[1] == "FULL" && DateTime.TryParse(parts[2], out var d))
            return (true, parts[0], d);
        return (false, null, null);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private StoredLicense? LoadStoredLicense()
    {
        try
        {
            if (!File.Exists(_licenseFile)) return null;
            return JsonSerializer.Deserialize<StoredLicense>(
                File.ReadAllText(_licenseFile, Encoding.UTF8));
        }
        catch { return null; }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }

    private sealed class StoredLicense { public string Key { get; set; } = ""; }
}
