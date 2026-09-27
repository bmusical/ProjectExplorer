using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Sharing;
using ProjectNest.Server;

namespace ProjectExplorer.Tests;

public class ShareCallerAddressTests
{
    [Fact]
    public void Create_StoresTheCallerAddressOnTheEvent()
    {
        var path = Path.Combine(Path.GetTempPath(), "nest-caller-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = SharingStore.Sqlite(path);
            var egg = NestEggCodec.Create(new Project { Name = "Geneology" }, "MIGHTYK10", "1.1.0");
            var json = NestEggCodec.ToJson(egg);
            var published = store.Create(egg, json, "MIGHTYK10", DateTime.UtcNow.AddDays(7), "203.0.113.9");

            var events = store.ReadEvents(ShareCodes.Normalize(published.Code));

            Assert.Contains(events, evt => evt.EventType == "Created" && evt.CallerAddress == "203.0.113.9");
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                var candidate = path + suffix;
                if (File.Exists(candidate)) File.Delete(candidate);
            }
            catch { /* the assertion does not depend on deleting the temp file */ }
        }
    }
}
