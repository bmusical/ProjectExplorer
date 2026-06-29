using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectExplorer.Core.Models;

namespace ProjectExplorer.Core.Services;

/// <summary>
/// Manages trial state and license key verification for Project Nest.
///
/// Keys are ECDSA-signed payloads: "email|FULL|yyyy-MM-dd"
/// Verification uses the embedded public key — no network call required.
///
/// Trial install date is stored in two places (registry + appdata file) so
/// resetting one location alone doesn't silently extend the trial.
/// </summary>
public sealed class LicenseManager
{
    // ── Configuration ────────────────────────────────────────────────────────

    private const int TrialDays = 30;
    private const string RegistryKeyPath = @"Software\HxM Blazor Software LLC\Project Nest";
    private const string RegistryValueName = "InstallDate";

    // Replace this with your real ECDSA P-256 public key (PEM, no headers) after
    // running: dotnet run --project tools/KeyGen  (see KeyGen instructions in README)
    // For development/testing the signature check is bypassed when this sentinel is present.
    private const string PublicKeyPem =
        "DEVELOPMENT_KEY_PLACEHOLDER";

    // ── Storage paths ─────────────────────────────────────────────────────────

    private readonly string _storageDir;
    private readonly string _licenseFile;    // %APPDATA%\ProjectExplorer\license.json
    private readonly string _trialFile;     // %APPDATA%\ProjectExplorer\trial.dat

    public LicenseManager() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ProjectExplorer"))
    { }

    internal LicenseManager(string storageDir)
    {
        _storageDir = storageDir;
        _licenseFile = Path.Combine(storageDir, "license.json");
        _trialFile   = Path.Combine(storageDir, "trial.dat");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public LicenseInfo GetCurrentLicense()
    {
        // 1. Valid stored license key?
        var stored = LoadStoredLicense();
        if (stored != null)
        {
            var (valid, email, date) = VerifyKey(stored.Key);
            if (valid)
                return new LicenseInfo { State = LicenseState.Licensed, Email = email, LicensedOn = date };

            // Key on disk but signature invalid — treat as invalid, fall through to trial
            return new LicenseInfo { State = LicenseState.Invalid };
        }

        // 2. Trial window
        var installDate = GetOrRecordInstallDate();
        var daysUsed = (DateTime.UtcNow.Date - installDate.Date).Days;
        var remaining = Math.Max(0, TrialDays - daysUsed);

        return remaining > 0
            ? new LicenseInfo { State = LicenseState.Trial, TrialDaysRemaining = remaining }
            : new LicenseInfo { State = LicenseState.TrialExpired, TrialDaysRemaining = 0 };
    }

    /// <summary>
    /// Attempts to activate with the supplied key. Returns the resulting LicenseInfo.
    /// On success the key is persisted to disk.
    /// </summary>
    public LicenseInfo Activate(string key)
    {
        key = key.Trim();
        var (valid, email, date) = VerifyKey(key);

        if (!valid)
            return new LicenseInfo { State = LicenseState.Invalid };

        Directory.CreateDirectory(_storageDir);
        var payload = JsonSerializer.Serialize(new StoredLicense { Key = key });
        File.WriteAllText(_licenseFile, payload, Encoding.UTF8);

        return new LicenseInfo { State = LicenseState.Licensed, Email = email, LicensedOn = date };
    }

    public void Deactivate()
    {
        if (File.Exists(_licenseFile))
            File.Delete(_licenseFile);
    }

    // ── Key verification ──────────────────────────────────────────────────────

    private (bool valid, string? email, DateTime? date) VerifyKey(string licenseKey)
    {
        // Dev mode: bypass crypto when placeholder key is present
        if (PublicKeyPem == "DEVELOPMENT_KEY_PLACEHOLDER")
            return ParsePayloadUnchecked(licenseKey);

        try
        {
            // Key format: Base64Url(payload bytes) + "." + Base64Url(ECDSA signature)
            var parts = licenseKey.Split('.');
            if (parts.Length != 2) return (false, null, null);

            var payloadBytes = Base64UrlDecode(parts[0]);
            var signature    = Base64UrlDecode(parts[1]);
            var payload      = Encoding.UTF8.GetString(payloadBytes);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(PublicKeyPem);

            bool ok = ecdsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256);
            if (!ok) return (false, null, null);

            return ParsePayload(payload);
        }
        catch
        {
            return (false, null, null);
        }
    }

    // Payload format: "email@example.com|FULL|2026-06-29"
    private static (bool, string?, DateTime?) ParsePayload(string payload)
    {
        var parts = payload.Split('|');
        if (parts.Length != 3 || parts[1] != "FULL") return (false, null, null);
        if (!DateTime.TryParse(parts[2], out var date)) return (false, null, null);
        return (true, parts[0], date);
    }

    // Used only in dev mode — accepts "email|FULL|date" directly as the key
    private static (bool, string?, DateTime?) ParsePayloadUnchecked(string key)
    {
        var parts = key.Split('|');
        if (parts.Length == 3 && parts[1] == "FULL" && DateTime.TryParse(parts[2], out var d))
            return (true, parts[0], d);
        return (false, null, null);
    }

    // ── Trial date tracking ───────────────────────────────────────────────────

    private DateTime GetOrRecordInstallDate()
    {
        // Try reading from both locations; use the earliest date found (most honest).
        DateTime? regDate  = ReadRegistryDate();
        DateTime? fileDate = ReadTrialFileDate();

        var earliest = new[] { regDate, fileDate }
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .DefaultIfEmpty(DateTime.UtcNow)
            .Min();

        // Persist to whichever location is missing
        if (regDate == null)  WriteRegistryDate(earliest);
        if (fileDate == null) WriteTrialFileDate(earliest);

        return earliest;
    }

    private static DateTime? ReadRegistryDate()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            var val = key?.GetValue(RegistryValueName) as string;
            return val != null && DateTime.TryParse(val, out var d) ? d : null;
        }
        catch { return null; }
    }

    private static void WriteRegistryDate(DateTime date)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .CreateSubKey(RegistryKeyPath, writable: true);
            key?.SetValue(RegistryValueName, date.ToString("o"));
        }
        catch { }
    }

    private DateTime? ReadTrialFileDate()
    {
        try
        {
            if (!File.Exists(_trialFile)) return null;
            var raw = File.ReadAllText(_trialFile, Encoding.UTF8).Trim();
            return DateTime.TryParse(raw, out var d) ? d : null;
        }
        catch { return null; }
    }

    private void WriteTrialFileDate(DateTime date)
    {
        try
        {
            Directory.CreateDirectory(_storageDir);
            File.WriteAllText(_trialFile, date.ToString("o"), Encoding.UTF8);
        }
        catch { }
    }

    // ── Stored license ────────────────────────────────────────────────────────

    private StoredLicense? LoadStoredLicense()
    {
        try
        {
            if (!File.Exists(_licenseFile)) return null;
            var json = File.ReadAllText(_licenseFile, Encoding.UTF8);
            return JsonSerializer.Deserialize<StoredLicense>(json);
        }
        catch { return null; }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "=";  break;
        }
        return Convert.FromBase64String(s);
    }

    private sealed class StoredLicense
    {
        public string Key { get; set; } = "";
    }
}
