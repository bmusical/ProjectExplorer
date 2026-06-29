using System.Drawing;
using ProjectExplorer.Shell.Interop;

namespace ProjectExplorer.Shell.Services;

/// <summary>
/// Implementation of IShellIconProvider using SHGetFileInfo P/Invoke.
/// </summary>
public class ShellIconProvider : IShellIconProvider
{
    public Icon GetFileIcon(string path, IconSize size, bool isFolder = false)
    {
        var flags = ShellNativeMethods.SHGFI_ICON |
                    ShellNativeMethods.SHGFI_SYSICONINDEX;

        if (size == IconSize.Small)
            flags |= ShellNativeMethods.SHGFI_SMALLICON;
        else
            flags |= ShellNativeMethods.SHGFI_LARGEICON;

        if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
        {
            // File doesn't exist — use USEFILEATTRIBUTES to get the icon by path/extension
            flags |= ShellNativeMethods.SHGFI_USEFILEATTRIBUTES;
            if (isFolder)
                flags |= (uint)ShellNativeMethods.FILE_ATTRIBUTE_DIRECTORY;
        }

        var shfi = new ShellNativeMethods.SHFILEINFO();
        var result = ShellNativeMethods.SHGetFileInfo(
            path,
            isFolder ? ShellNativeMethods.FILE_ATTRIBUTE_DIRECTORY : ShellNativeMethods.FILE_ATTRIBUTE_NORMAL,
            ref shfi,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf(shfi),
            flags);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
        {
            // Fallback: return a default icon
            return SystemIcons.Application;
        }

        try
        {
            return Icon.FromHandle(shfi.hIcon);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public Icon GetFolderIcon(IconSize size, bool open = false)
    {
        var flags = ShellNativeMethods.SHGFI_ICON |
                    ShellNativeMethods.SHGFI_SYSICONINDEX |
                    ShellNativeMethods.SHGFI_USEFILEATTRIBUTES;

        if (open)
            flags |= ShellNativeMethods.SHGFI_OPENICON;

        if (size == IconSize.Small)
            flags |= ShellNativeMethods.SHGFI_SMALLICON;
        else
            flags |= ShellNativeMethods.SHGFI_LARGEICON;

        var shfi = new ShellNativeMethods.SHFILEINFO();
        var result = ShellNativeMethods.SHGetFileInfo(
            "dummy_folder",
            ShellNativeMethods.FILE_ATTRIBUTE_DIRECTORY,
            ref shfi,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf(shfi),
            flags);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return SystemIcons.Application;

        try
        {
            return Icon.FromHandle(shfi.hIcon);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public Icon GetIconByExtension(string extension, IconSize size)
    {
        if (!extension.StartsWith("."))
            extension = "." + extension;

        var flags = ShellNativeMethods.SHGFI_ICON |
                    ShellNativeMethods.SHGFI_SYSICONINDEX |
                    ShellNativeMethods.SHGFI_USEFILEATTRIBUTES;

        if (size == IconSize.Small)
            flags |= ShellNativeMethods.SHGFI_SMALLICON;
        else
            flags |= ShellNativeMethods.SHGFI_LARGEICON;

        var shfi = new ShellNativeMethods.SHFILEINFO();
        var result = ShellNativeMethods.SHGetFileInfo(
            extension,
            ShellNativeMethods.FILE_ATTRIBUTE_NORMAL,
            ref shfi,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf(shfi),
            flags);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return SystemIcons.Application;

        try
        {
            return Icon.FromHandle(shfi.hIcon);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public Bitmap IconToBitmap(Icon icon, int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.DrawIcon(icon, new Rectangle(0, 0, size, size));
        return bmp;
    }
}
