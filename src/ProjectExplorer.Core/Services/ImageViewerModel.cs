namespace ProjectExplorer.Core.Services;

/// <summary>
/// UI-agnostic navigation state for the image viewer: holds the ordered list of
/// image paths in a folder and the current index, and exposes Next/Previous
/// movement. Separated from the WinForms layer so it can be unit-tested.
/// </summary>
public class ImageViewerModel
{
    private readonly List<string> _images;
    private int _index;

    public ImageViewerModel(IEnumerable<string> images, string current)
    {
        _images = images?.ToList() ?? new List<string>();
        _index = _images.FindIndex(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
        if (_index < 0) _index = 0;
    }

    public int Count => _images.Count;

    public int Index => _index;

    public bool HasImages => _images.Count > 0;

    public string? Current =>
        (_index >= 0 && _index < _images.Count) ? _images[_index] : null;

    /// <summary>Advance to the next image, wrapping to the first. Returns the new current path.</summary>
    public string? Next()
    {
        if (_images.Count == 0) return null;
        _index = (_index + 1) % _images.Count;
        return Current;
    }

    /// <summary>Move to the previous image, wrapping to the last. Returns the new current path.</summary>
    public string? Previous()
    {
        if (_images.Count == 0) return null;
        _index = (_index - 1 + _images.Count) % _images.Count;
        return Current;
    }
}
