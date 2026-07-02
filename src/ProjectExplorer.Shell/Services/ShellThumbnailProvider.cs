using System.Drawing;
using ProjectExplorer.Shell.Interop;

namespace ProjectExplorer.Shell.Services;

/// <summary>
/// Implementation of <see cref="IShellThumbnailProvider"/> using the Windows
/// Shell <c>IShellItemImageFactory</c> API. Produces real image previews for
/// picture files (and shell-provided thumbnails for other file types) with a
/// graceful null return when a thumbnail cannot be produced, so callers can
/// fall back to a normal icon.
/// </summary>
public class ShellThumbnailProvider : IShellThumbnailProvider
{
    public Bitmap? GetThumbnail(string path, Size size)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return null;

        if (size.Width <= 0 || size.Height <= 0)
            return null;

        IShellItemImageFactory? factory = null;
        IntPtr hBitmap = IntPtr.Zero;

        try
        {
            var iid = ThumbnailNativeMethods.IID_IShellItemImageFactory;
            ThumbnailNativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (factory == null)
                return null;

            var nativeSize = new ThumbnailNativeMethods.SIZE(size.Width, size.Height);

            // Prefer a real thumbnail, but allow the shell to fall back to an
            // icon so unsupported types still return something usable.
            var flags = ThumbnailNativeMethods.SIIGBF.ResizeToFit;

            var hr = factory.GetImage(nativeSize, flags, out hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
                return null;

            // Copy the HBITMAP into a managed Bitmap we fully own.
            using var fromHandle = Image.FromHbitmap(hBitmap);
            return new Bitmap(fromHandle);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
                ThumbnailNativeMethods.DeleteObject(hBitmap);
            if (factory != null)
                System.Runtime.InteropServices.Marshal.ReleaseComObject(factory);
        }
    }
}
