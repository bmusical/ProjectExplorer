namespace ProjectExplorer.Shell;

/// <summary>
/// Service for performing file operations using the Windows Shell API,
/// which provides proper progress dialogs, Recycle Bin support, and undo.
/// </summary>
public interface IShellFileOperations
{
    void Copy(string[] sources, string destination);
    void Move(string[] sources, string destination);
    void Delete(string[] paths, bool recycle = true);
    void Rename(string path, string newName);
}
