using ProjectExplorer.Core.Sharing;

namespace ProjectExplorer.Tests;

public class NestEggErrorTests
{
    [Fact]
    public void Parse_NamesTheFieldWhenAGuidIsInvalid()
    {
        var json = """
        {
          "schemaVersion": 1,
          "kind": "project-nest-egg",
          "source": { "machineLabel": "outliner", "appVersion": "outline-1" },
          "project": {
            "sourceId": "11111111-1111-4111-8111-111111111111",
            "name": "Video",
            "nodes": [
              {
                "sourceId": "not-a-guid",
                "parentSourceId": "11111111-1111-4111-8111-111111111111",
                "childType": "collection",
                "sortOrder": 0,
                "name": "Cold open"
              }
            ]
          }
        }
        """;

        var error = Assert.Throws<NestEggFormatException>(() => NestEggCodec.Parse(json));

        Assert.Contains("nodes[0].sourceId", error.Message);
        Assert.Contains("GUID", error.Message);
        Assert.Contains("Line ", error.Message);
        Assert.DoesNotContain("System.Guid", error.Message);
    }

    [Fact]
    public void Validate_NamesTheCollectionThatNeedsAName()
    {
        var projectId = Guid.NewGuid();
        var egg = new NestEggDocument
        {
            Source = new NestEggSource { MachineLabel = "outliner", AppVersion = NestEggOutline.ProfileVersion },
            Project = new NestEggProject
            {
                SourceId = projectId,
                Name = "Video",
                Nodes =
                [
                    new NestEggNode
                    {
                        SourceId = Guid.NewGuid(),
                        ParentSourceId = projectId,
                        ChildType = "collection",
                        Name = "Open",
                        SortOrder = 0
                    },
                    new NestEggNode
                    {
                        SourceId = Guid.NewGuid(),
                        ParentSourceId = projectId,
                        ChildType = "collection",
                        Name = "  ",
                        SortOrder = 1
                    }
                ]
            }
        };

        var error = Assert.Throws<NestEggFormatException>(() => NestEggOutline.Validate(egg));

        Assert.Contains("nodes[1]", error.Message);
        Assert.Contains("needs a name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_SaysTheFileSizeWhenItIsOverTheLimit()
    {
        var json = new string('{', NestEggLimits.MaxJsonBytes + 1);

        var error = Assert.Throws<NestEggFormatException>(() => NestEggCodec.Parse(json));

        Assert.Contains((NestEggLimits.MaxJsonBytes + 1).ToString("N0"), error.Message);
        Assert.Contains(NestEggLimits.MaxJsonBytes.ToString("N0"), error.Message);
        Assert.Contains("Split it", error.Message);
    }

    [Fact]
    public void Parse_RejectsTheWrongSchemaWithoutMentioningAServer()
    {
        var json = """
        {
          "schemaVersion": 9,
          "kind": "project-nest-egg",
          "source": { "machineLabel": "outliner" },
          "project": { "sourceId": "11111111-1111-4111-8111-111111111111", "name": "Video", "nodes": [] }
        }
        """;

        var error = Assert.Throws<NestEggFormatException>(() => NestEggCodec.Parse(json));

        Assert.Contains("schema 9", error.Message);
        Assert.Contains("schema 1", error.Message);
        Assert.DoesNotContain("server", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForImportDialog_LeadsWithTheFileName()
    {
        var text = NestEggErrors.ForImportDialog(
            new NestEggFormatException("nodes[3] \"Cold open\" needs a name."),
            Path.Combine("drafts", "video.nestegg.json"));

        Assert.StartsWith("video.nestegg.json", text);
        Assert.Contains("nodes[3]", text);
        Assert.Contains("Cold open", text);
    }
}
