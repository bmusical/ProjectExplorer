namespace ProjectExplorer.Tests;

public class SharingSqlScriptTests
{
    [Fact]
    public void CreateScript_DefinesTheSharingProcedures()
    {
        var path = FindScript();
        var sql = File.ReadAllText(path);

        Assert.Contains("CREATE DATABASE ProjectNestSharing", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var procedure in new[]
        {
            "dbo.usp_Share_CodeExists",
            "dbo.usp_Share_Create",
            "dbo.usp_Share_GetByCode",
            "dbo.usp_ShareEvent_Insert",
            "dbo.usp_Share_RecordFetch",
            "dbo.usp_Share_RecordImport",
            "dbo.usp_Share_Revoke",
            "dbo.usp_ShareEvent_List"
        })
        {
            Assert.Contains("CREATE OR ALTER PROCEDURE " + procedure, sql, StringComparison.Ordinal);
        }

        Assert.Contains("dbo.NestEggs", sql, StringComparison.Ordinal);
        Assert.Contains("dbo.Shares", sql, StringComparison.Ordinal);
        Assert.Contains("dbo.ShareEvents", sql, StringComparison.Ordinal);
    }

    private static string FindScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ProjectNest.Server", "Sql", "001_CreateSharingDatabase.sql");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("001_CreateSharingDatabase.sql was not found above the test output directory.");
    }
}
