using ProjectNest.Server;

namespace ProjectExplorer.Tests;

public class SharingSqlScriptTests
{
    [Fact]
    public void UseDatabase_WritesProjectNestSharingIntoTheConnection()
    {
        const string local = "Server=MIGHTYK10\\SQLEXPRESS;Database=db_acdaa1_conproddb;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=true;";

        var sharing = SharingConnection.UseDatabase(local);

        Assert.Contains("Initial Catalog=ProjectNestSharing", sharing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"Data Source=MIGHTYK10\SQLEXPRESS", sharing, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db_acdaa1_conproddb", sharing, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateScript_DefinesTheSharingProcedures()
    {
        var path = FindScript();
        var sql = File.ReadAllText(path);

        Assert.Contains("CREATE DATABASE ProjectNestSharing", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CallerAddress", sql, StringComparison.Ordinal);
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
