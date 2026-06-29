/// <summary>
/// Project Nest — License Key Generator
/// HxM Blazor Software LLC — INTERNAL USE ONLY, do not distribute.
///
/// Usage:
///   dotnet run -- generate --email customer@example.com --key private_key.pem
///   dotnet run -- setup
///   dotnet run -- verify --license "XXXX.YYYY" --key public_key.pem
/// </summary>

using System.Security.Cryptography;
using System.Text;

var command = args.FirstOrDefault() ?? "help";

switch (command.ToLower())
{
    case "setup":
        RunSetup();
        break;
    case "generate":
        RunGenerate(args);
        break;
    case "verify":
        RunVerify(args);
        break;
    default:
        PrintHelp();
        break;
}

// ── Commands ──────────────────────────────────────────────────────────────────

static void RunSetup()
{
    Console.WriteLine("=== Project Nest — Keypair Generator ===");
    Console.WriteLine("Generating ECDSA P-256 keypair...\n");

    using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    var publicPem  = ecdsa.ExportSubjectPublicKeyInfoPem();
    var privatePem = ecdsa.ExportPkcs8PrivateKeyPem();

    // Save to files
    File.WriteAllText("public_key.pem",  publicPem,  Encoding.UTF8);
    File.WriteAllText("private_key.pem", privatePem, Encoding.UTF8);

    Console.WriteLine("PUBLIC KEY (paste into LicenseManager.cs → PublicKeyPem):");
    Console.WriteLine(publicPem);
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("PRIVATE KEY saved to: private_key.pem");
    Console.WriteLine("Keep this file OFFLINE and NEVER commit it to source control.");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("Files written: public_key.pem, private_key.pem");
}

static void RunGenerate(string[] args)
{
    var email      = GetArg(args, "--email");
    var keyFile    = GetArg(args, "--key") ?? "private_key.pem";

    if (email == null)
    {
        Console.Error.WriteLine("Error: --email is required.");
        Console.Error.WriteLine("Usage: dotnet run -- generate --email customer@example.com [--key private_key.pem]");
        Environment.Exit(1);
    }

    if (!File.Exists(keyFile))
    {
        Console.Error.WriteLine($"Error: private key file not found: {keyFile}");
        Console.Error.WriteLine("Run 'dotnet run -- setup' first to generate a keypair.");
        Environment.Exit(1);
    }

    var privatePem = File.ReadAllText(keyFile, Encoding.UTF8);
    var date       = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var payload    = $"{email}|FULL|{date}";

    using var ecdsa = ECDsa.Create();
    ecdsa.ImportFromPem(privatePem);

    var payloadBytes = Encoding.UTF8.GetBytes(payload);
    var signature    = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256);

    var licenseKey = $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";

    Console.WriteLine();
    Console.WriteLine($"  Customer : {email}");
    Console.WriteLine($"  Date     : {date}");
    Console.WriteLine($"  Payload  : {payload}");
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("LICENSE KEY (send this to the customer):");
    Console.WriteLine();
    Console.WriteLine(licenseKey);
    Console.ResetColor();
    Console.WriteLine();
}

static void RunVerify(string[] args)
{
    var licenseKey = GetArg(args, "--license");
    var keyFile    = GetArg(args, "--key") ?? "public_key.pem";

    if (licenseKey == null)
    {
        Console.Error.WriteLine("Error: --license is required.");
        Environment.Exit(1);
    }

    if (!File.Exists(keyFile))
    {
        Console.Error.WriteLine($"Error: public key file not found: {keyFile}");
        Environment.Exit(1);
    }

    var publicPem = File.ReadAllText(keyFile, Encoding.UTF8);

    try
    {
        var parts = licenseKey.Split('.');
        if (parts.Length != 2) throw new FormatException("Expected format: payload.signature");

        var payloadBytes = Base64UrlDecode(parts[0]);
        var signature    = Base64UrlDecode(parts[1]);
        var payload      = Encoding.UTF8.GetString(payloadBytes);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(publicPem);

        bool valid = ecdsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256);

        if (valid)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✔  VALID key");
            Console.ResetColor();
            Console.WriteLine($"   Payload: {payload}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("✘  INVALID signature — key has been tampered with.");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✘  Error verifying key: {ex.Message}");
        Console.ResetColor();
        Environment.Exit(1);
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static void PrintHelp()
{
    Console.WriteLine("""
        Project Nest License Key Generator — HxM Blazor Software LLC

        Commands:
          setup
              Generate a new ECDSA keypair. Run once, keep private_key.pem offline.

          generate --email <email> [--key <private_key.pem>]
              Generate a signed license key for a customer.

          verify --license <key> [--key <public_key.pem>]
              Verify that a license key is genuine.

        Examples:
          dotnet run -- setup
          dotnet run -- generate --email john@example.com
          dotnet run -- generate --email john@example.com --key C:\Keys\private_key.pem
          dotnet run -- verify --license "abc123.xyz789"
        """);
}

static string? GetArg(string[] args, string flag)
{
    var idx = Array.IndexOf(args, flag);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static string Base64UrlEncode(byte[] data)
{
    return Convert.ToBase64String(data)
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

static byte[] Base64UrlDecode(string s)
{
    s = s.Replace('-', '+').Replace('_', '/');
    switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
    return Convert.FromBase64String(s);
}
