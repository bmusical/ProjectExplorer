using System.Security.Cryptography;

namespace ProjectNest.Server;

/// <summary>
/// Eight-character codes from an alphabet that avoids 0/O and 1/I/L, shown to
/// the user as ABCD-EFGH and stored without the hyphen.
/// </summary>
public static class ShareCodes
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var chars = code.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray();
        return new string(chars).ToUpperInvariant();
    }

    public static string Format(string canonical) =>
        canonical.Length == 8 ? canonical[..4] + "-" + canonical[4..] : canonical;

    public static bool IsWellFormed(string canonical) =>
        canonical.Length == 8 && canonical.All(c => Alphabet.Contains(c));
}
