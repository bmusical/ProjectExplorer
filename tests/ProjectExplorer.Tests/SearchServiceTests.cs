using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;

namespace ProjectExplorer.Tests;

public class SearchServiceTests
{
    [Fact]
    public void Search_EmptyOrWhitespaceQuery_ReturnsNoResults()
    {
        var projects = new List<Project> { new() { Name = "Anything" } };

        Assert.Empty(SearchService.Search(projects, ""));
        Assert.Empty(SearchService.Search(projects, "   "));
    }

    [Fact]
    public void Search_MatchesProjectName()
    {
        var project = new Project { Name = "Client Website Redesign" };

        var results = SearchService.Search([project], "website");

        var result = Assert.Single(results);
        Assert.Equal(project.Id, result.ProjectId);
        Assert.Null(result.ChildId);
        Assert.Null(result.ChildType);
        Assert.Equal("Name", result.MatchedField);
    }

    [Fact]
    public void Search_MatchesProjectDescription()
    {
        var project = new Project { Name = "P1", Description = "Handles invoicing for Acme Corp" };

        var results = SearchService.Search([project], "invoicing");

        var result = Assert.Single(results);
        Assert.Equal("Description", result.MatchedField);
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var project = new Project { Name = "NinjaTech Platform" };

        Assert.Single(SearchService.Search([project], "NINJATECH"));
        Assert.Single(SearchService.Search([project], "ninjatech"));
    }

    [Fact]
    public void Search_MatchesCollectionNameAndDescriptionRecursively()
    {
        var project = new Project { Name = "P1" };
        var top = new Collection { Name = "Assets" };
        var sub = new Collection { Name = "Textures", Description = "Diffuse and normal maps" };
        top.Children.Add(sub);
        project.Children.Add(top);

        var byName = SearchService.Search([project], "textures");
        var byNameResult = Assert.Single(byName);
        Assert.Equal(sub.Id, byNameResult.ChildId);
        Assert.Equal(ChildType.Collection, byNameResult.ChildType);

        var byDescription = SearchService.Search([project], "normal maps");
        Assert.Equal("Description", Assert.Single(byDescription).MatchedField);
    }

    [Fact]
    public void Search_MatchesFolderReferenceNameDescriptionAndPath()
    {
        var project = new Project { Name = "P1" };
        var fr = new FolderReference { RealPath = @"C:\Dev\MyProject", DisplayName = "Active Code", Description = "Main working copy" };
        project.Children.Add(fr);

        Assert.Equal("Name", Assert.Single(SearchService.Search([project], "active code")).MatchedField);
        Assert.Equal("Description", Assert.Single(SearchService.Search([project], "working copy")).MatchedField);
        Assert.Equal("Path", Assert.Single(SearchService.Search([project], @"Dev\MyProject")).MatchedField);
    }

    [Fact]
    public void Search_MatchesWebResourceUrl()
    {
        // DisplayName set explicitly so EffectiveName doesn't fall back to a URL-derived host
        // name (which would otherwise match "Name" before "URL" is even checked).
        var project = new Project { Name = "P1" };
        var wr = new WebResource { Url = "https://docs.example.com/api", DisplayName = "API Docs", Description = "Reference material" };
        project.Children.Add(wr);

        var result = Assert.Single(SearchService.Search([project], "docs.example.com"));
        Assert.Equal("URL", result.MatchedField);
        Assert.Equal(ChildType.WebResource, result.ChildType);
    }

    [Fact]
    public void Search_MatchesFileReferencePath()
    {
        var project = new Project { Name = "P1" };
        var fileRef = new FileReference { FilePath = @"C:\Docs\spec.pdf" };
        project.Children.Add(fileRef);

        var result = Assert.Single(SearchService.Search([project], "spec.pdf"));
        Assert.Equal(ChildType.FileReference, result.ChildType);
    }

    [Fact]
    public void Search_MatchesMetadataValues()
    {
        var project = new Project { Name = "P1" };
        var fr = new FolderReference { RealPath = @"C:\Dev" };
        fr.Metadata["note"] = "flaky network drive, verify before relying on it";
        project.Children.Add(fr);

        var result = Assert.Single(SearchService.Search([project], "flaky network"));
        Assert.Equal("Metadata: note", result.MatchedField);
    }

    [Fact]
    public void Search_OneResultPerNode_EvenWhenMultipleFieldsMatch()
    {
        var project = new Project { Name = "P1" };
        var fr = new FolderReference { RealPath = @"C:\Dev\widget", DisplayName = "widget", Description = "the widget folder" };
        project.Children.Add(fr);

        var results = SearchService.Search([project], "widget");

        var result = Assert.Single(results);
        Assert.Equal("Name", result.MatchedField); // Name is checked before Description/Path
    }

    [Fact]
    public void Search_ReturnsMatchesAcrossMultipleProjects()
    {
        var p1 = new Project { Name = "Alpha" };
        var p2 = new Project { Name = "Beta" };
        p1.Children.Add(new FolderReference { RealPath = "C:\\shared-target" });
        p2.Children.Add(new FolderReference { RealPath = "C:\\shared-target" });

        var results = SearchService.Search([p1, p2], "shared-target");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.ProjectId == p1.Id);
        Assert.Contains(results, r => r.ProjectId == p2.Id);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmptyList()
    {
        var project = new Project { Name = "P1" };
        project.Children.Add(new FolderReference { RealPath = @"C:\Dev" });

        Assert.Empty(SearchService.Search([project], "does-not-exist-anywhere"));
    }
}
