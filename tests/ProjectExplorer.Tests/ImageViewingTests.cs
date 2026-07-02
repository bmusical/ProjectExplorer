using ProjectExplorer.Core.Services;

namespace ProjectExplorer.Tests;

public class ImageFileHelperTests
{
    [Theory]
    [InlineData(".jpg", true)]
    [InlineData(".JPG", true)]
    [InlineData("jpeg", true)]
    [InlineData(".png", true)]
    [InlineData(".gif", true)]
    [InlineData(".bmp", true)]
    [InlineData(".tif", true)]
    [InlineData(".tiff", true)]
    [InlineData(".ico", true)]
    [InlineData(".txt", false)]
    [InlineData(".pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsImageExtension_ClassifiesCorrectly(string? ext, bool expected)
    {
        Assert.Equal(expected, ImageFileHelper.IsImageExtension(ext));
    }

    [Theory]
    [InlineData(@"C:\photos\cat.png", true)]
    [InlineData(@"C:\photos\report.pdf", false)]
    [InlineData(@"C:\photos\no_extension", false)]
    [InlineData("/home/user/pic.JPEG", true)]
    [InlineData(null, false)]
    public void IsImageFile_ClassifiesCorrectly(string? path, bool expected)
    {
        Assert.Equal(expected, ImageFileHelper.IsImageFile(path));
    }
}

public class ImageViewerModelTests
{
    private static readonly string[] Images =
    {
        @"C:\p\a.png", @"C:\p\b.jpg", @"C:\p\c.gif"
    };

    [Fact]
    public void Ctor_SetsCurrentToGivenImage()
    {
        var m = new ImageViewerModel(Images, @"C:\p\b.jpg");
        Assert.Equal(1, m.Index);
        Assert.Equal(@"C:\p\b.jpg", m.Current);
        Assert.Equal(3, m.Count);
    }

    [Fact]
    public void Ctor_UnknownCurrent_DefaultsToFirst()
    {
        var m = new ImageViewerModel(Images, @"C:\p\zzz.png");
        Assert.Equal(0, m.Index);
    }

    [Fact]
    public void Next_AdvancesAndWraps()
    {
        var m = new ImageViewerModel(Images, @"C:\p\a.png");
        Assert.Equal(@"C:\p\b.jpg", m.Next());
        Assert.Equal(@"C:\p\c.gif", m.Next());
        Assert.Equal(@"C:\p\a.png", m.Next()); // wraps
    }

    [Fact]
    public void Previous_MovesBackAndWraps()
    {
        var m = new ImageViewerModel(Images, @"C:\p\a.png");
        Assert.Equal(@"C:\p\c.gif", m.Previous()); // wraps to last
        Assert.Equal(@"C:\p\b.jpg", m.Previous());
    }

    [Fact]
    public void EmptyList_IsSafe()
    {
        var m = new ImageViewerModel(System.Array.Empty<string>(), "");
        Assert.False(m.HasImages);
        Assert.Null(m.Current);
        Assert.Null(m.Next());
        Assert.Null(m.Previous());
    }
}
