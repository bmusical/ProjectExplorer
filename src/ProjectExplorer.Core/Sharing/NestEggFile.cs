namespace ProjectExplorer.Core.Sharing;

/// <summary>
/// Reads a Nest Egg document from a UTF-8 file. The WinForms import command
/// and tests both come through here so a missing or unreadable file fails
/// the same way as a document the codec rejects.
/// </summary>
public static class NestEggFile
{
    public static NestEggDocument Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new NestEggFormatException("The Nest Egg file needs a path.");

        try
        {
            return NestEggCodec.Parse(File.ReadAllText(path));
        }
        catch (NestEggFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new NestEggFormatException("Couldn't read the Nest Egg file. " + ex.Message);
        }
    }
}
