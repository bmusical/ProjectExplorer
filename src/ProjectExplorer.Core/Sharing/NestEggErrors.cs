using System.Text.Json;

namespace ProjectExplorer.Core.Sharing;

/// <summary>
/// Turns a failed Nest Import into a sentence that names the file, the node,
/// and the field. The raw serializer text is not what the dialog shows.
/// </summary>
public static class NestEggErrors
{
    public const int DialogLimit = 1_200;

    public static string ForImportDialog(Exception ex, string? filePath)
    {
        var name = string.IsNullOrWhiteSpace(filePath) ? "" : Path.GetFileName(filePath);
        var detail = ex switch
        {
            NestEggFormatException or InvalidOperationException => ex.Message,
            _ => "Couldn't import this file. " + ex.Message
        };
        detail = Truncate(detail.Trim(), DialogLimit);
        return string.IsNullOrEmpty(name) ? detail : name + Environment.NewLine + Environment.NewLine + detail;
    }

    public static string DescribeJsonException(JsonException ex)
    {
        var place = FormatPlace(ex);
        var reason = ClassifyJson(ex.Message);
        return string.IsNullOrEmpty(place) ? reason : place + ": " + reason;
    }

    /// <summary>
    /// <paramref name="index"/> is the position in <c>project.nodes</c>, starting at 0,
    /// the same index as in the JSON.
    /// </summary>
    public static string Item(NestEggNode? node, int index)
    {
        if (node == null)
            return $"nodes[{index}]";

        var label = FirstText(node.Name, node.DisplayName);
        if (node.Metadata != null
            && node.Metadata.TryGetValue(NestEggOutline.KeyMetadataKey, out var key)
            && !string.IsNullOrWhiteSpace(key))
        {
            var slug = Truncate(key.Trim(), 64);
            return label == null
                ? $"nodes[{index}] (key {slug})"
                : $"nodes[{index}] \"{label}\" (key {slug})";
        }

        return label == null ? $"nodes[{index}]" : $"nodes[{index}] \"{label}\"";
    }

    public static string TooLarge(int actualBytes)
    {
        return $"This file is {actualBytes:N0} bytes. A Nest Egg can be at most {NestEggLimits.MaxJsonBytes:N0} bytes. Split it into a smaller tree and import that.";
    }

    private static string? FormatPlace(JsonException ex)
    {
        var parts = new List<string>();
        if (ex.LineNumber is >= 0)
            parts.Add($"Line {ex.LineNumber.Value + 1}");
        var path = FriendlyPath(ex.Path);
        if (path != null)
            parts.Add(path);
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    internal static string? FriendlyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var trimmed = path.Trim();
        if (trimmed.StartsWith("$.", StringComparison.Ordinal))
            trimmed = trimmed[2..];
        else if (trimmed == "$")
            return "the top of the file";
        return trimmed;
    }

    private static string ClassifyJson(string? message)
    {
        var text = message ?? "";
        if (text.Contains("System.Guid", StringComparison.Ordinal))
            return "this value needs to be a GUID, like 11111111-1111-4111-8111-111111111111.";
        if (text.Contains("System.DateTime", StringComparison.Ordinal))
            return "this value needs to be a UTC time, like 2026-09-27T19:00:00Z.";
        if (text.Contains("System.Int32", StringComparison.Ordinal) || text.Contains("System.Int64", StringComparison.Ordinal))
            return "this value needs to be a whole number.";
        if (text.Contains("System.Boolean", StringComparison.Ordinal))
            return "this value needs to be true or false.";
        if (text.Contains("Dictionary", StringComparison.Ordinal))
            return "metadata needs to be an object of text keys and text values, or left out.";
        if (text.Contains("is an invalid start", StringComparison.OrdinalIgnoreCase)
            || text.Contains("invalid after", StringComparison.OrdinalIgnoreCase)
            || text.Contains("',' is invalid", StringComparison.Ordinal)
            || text.Contains("expected end", StringComparison.OrdinalIgnoreCase))
            return "the JSON is broken here. Look for a missing comma, an extra comma, or a value that was cut off.";

        var sentence = FirstSentence(text);
        return string.IsNullOrEmpty(sentence)
            ? "the JSON could not be read."
            : sentence;
    }

    private static string FirstSentence(string text)
    {
        var pipe = text.IndexOf('|');
        if (pipe >= 0)
            text = text[..pipe];
        var path = text.IndexOf(" Path:", StringComparison.Ordinal);
        if (path >= 0)
            text = text[..path];
        text = text.Trim().TrimEnd('.');
        return Truncate(text, 240);
    }

    private static string? FirstText(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return Truncate(value.Trim(), 80);
        }
        return null;
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..max] + "…";
    }
}
