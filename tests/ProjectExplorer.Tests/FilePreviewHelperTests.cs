using ProjectExplorer.Core.Services;

namespace ProjectExplorer.Tests;

public class FilePreviewHelperTests
{
    [Theory]
    [InlineData(".txt", true)]
    [InlineData(".MD", true)]
    [InlineData("json", true)]
    [InlineData(".cs", true)]
    [InlineData(".png", false)]
    [InlineData(".pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTextExtension_ClassifiesCorrectly(string? ext, bool expected)
    {
        Assert.Equal(expected, FilePreviewHelper.IsTextExtension(ext));
    }

    [Theory]
    [InlineData(@"C:\notes\todo.txt", true)]
    [InlineData(@"C:\src\Program.cs", true)]
    [InlineData(@"C:\photos\cat.png", false)]
    [InlineData(@"C:\reports\q1.pdf", false)]
    [InlineData(@"C:\files\no_extension", false)]
    [InlineData(null, false)]
    public void IsTextFile_ClassifiesCorrectly(string? path, bool expected)
    {
        Assert.Equal(expected, FilePreviewHelper.IsTextFile(path));
    }

    [Theory]
    [InlineData(@"C:\photos\cat.png", FilePreviewKind.Image)]
    [InlineData(@"C:\notes\todo.txt", FilePreviewKind.Text)]
    [InlineData(@"C:\reports\q1.pdf", FilePreviewKind.None)]
    [InlineData(null, FilePreviewKind.None)]
    public void GetPreviewKind_ClassifiesCorrectly(string? path, FilePreviewKind expected)
    {
        Assert.Equal(expected, FilePreviewHelper.GetPreviewKind(path));
    }
}
