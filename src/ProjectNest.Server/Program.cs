using System.Text;
using ProjectExplorer.Core.Sharing;
using ProjectNest.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<SharingOptions>(builder.Configuration.GetSection(SharingOptions.SectionName));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = NestEggLimits.MaxJsonBytes + 100_000);

var app = builder.Build();
var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SharingOptions>>().Value;
SharingStore store;
// Sharing:ConnectionString wins. An explicit Sharing:DatabasePath (the tests)
// stays on SQLite. Otherwise Development uses ConnectionStrings:ControlPlane
// (ProjectNestSharing). DefaultConnection is a different database and is not read here.
var sqlConnection = ResolveSqlConnection(options, app.Configuration);
if (sqlConnection != null)
{
    store = SharingStore.SqlServer(sqlConnection.Value);
    app.Logger.LogInformation(
        "Sharing database is SQL Server (ProjectNestSharing) via {Source}.",
        sqlConnection.Source);
}
else
{
    var databasePath = string.IsNullOrWhiteSpace(options.DatabasePath)
        ? Path.Combine(app.Environment.ContentRootPath, "data", "sharing.db")
        : options.DatabasePath;
    store = SharingStore.Sqlite(databasePath);
    app.Logger.LogInformation(
        "Sharing database is the local SQLite file {Path}. Run Sql/001_CreateSharingDatabase.sql, then set ConnectionStrings:ControlPlane or Sharing:ConnectionString to use SQL Server.",
        databasePath);
}

app.MapGet("/", () => Results.Text(
    "Project Nest sharing server is running.\nUse File > Share Project in Project Nest Explorer, pointed at this address.\n",
    "text/plain"));

app.MapGet("/api/health", () => Results.Ok(new { service = "project-nest-sharing", phase = 1 }));

app.MapPost("/api/shares", (PublishShareRequest? request) =>
{
    if (request?.Egg == null)
        return Results.BadRequest(new ShareApiError { Error = "The request is missing the Nest Egg." });

    string machineLabel;
    try
    {
        machineLabel = NestEggCodec.RequireMachineLabel(request.MachineLabel);
        request.Egg.Source ??= new NestEggSource();
        request.Egg.Source.MachineLabel = machineLabel;
        NestEggCodec.Validate(request.Egg);
    }
    catch (NestEggFormatException ex)
    {
        return Results.BadRequest(new ShareApiError { Error = ex.Message });
    }

    var json = NestEggCodec.ToJson(request.Egg);
    if (Encoding.UTF8.GetByteCount(json) > NestEggLimits.MaxJsonBytes)
        return Results.BadRequest(new ShareApiError { Error = $"The Nest Egg is larger than {NestEggLimits.MaxJsonBytes:N0} bytes." });

    var published = store.Create(request.Egg, json, machineLabel, options.ExpiresFrom(DateTime.UtcNow));
    app.Logger.LogInformation("Share {Code} stored for {ProjectName} ({Bytes} bytes)",
        published.Code, published.ProjectName, published.ByteLength);
    return Results.Ok(published);
});

app.MapGet("/api/shares/{code}", (string code, string? machine) =>
    Lookup(code, store.Preview, machine));

app.MapGet("/api/shares/{code}/egg", (string code, string? machine, HttpResponse response) =>
{
    if (!TryCanonical(code, out var canonical, out var error))
        return error!;

    var lookup = store.Fetch(canonical, machine);
    if (lookup.Status != ShareStatus.Available || lookup.Value == null)
        return StatusFor(lookup.Status, lookup.Error);

    app.Logger.LogInformation("Share {Code} fetched", ShareCodes.Format(canonical));
    response.Headers["X-Payload-Sha256"] = lookup.Value.Sha256;
    return Results.Text(lookup.Value.Json, "application/json; charset=utf-8");
});

app.MapGet("/api/shares/{code}/events", (string code) =>
    Lookup(code, (canonical, _) => store.Events(canonical), null));

app.MapPost("/api/shares/{code}/events", (string code, ShareEventRequest? request) =>
{
    if (!ShareEventTypes.IsClientReportable(request?.EventType))
        return Results.BadRequest(new ShareApiError { Error = "Only an Imported event can be reported by a client." });
    return Lookup(code, (canonical, machine) => store.ReportImported(canonical, machine ?? request?.MachineLabel, request?.Detail), request?.MachineLabel);
});

app.MapDelete("/api/shares/{code}", (string code, string? machine) =>
{
    if (!TryCanonical(code, out var canonical, out var error))
        return error!;
    var lookup = store.Revoke(canonical, machine);
    if (lookup.Status != ShareStatus.Available)
        return StatusFor(lookup.Status, lookup.Error);
    app.Logger.LogInformation("Share {Code} revoked", ShareCodes.Format(canonical));
    return Results.Ok(new { revoked = true });
});

app.Run();

static SqlConnectionChoice? ResolveSqlConnection(SharingOptions options, IConfiguration configuration)
{
    if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        return new SqlConnectionChoice(options.ConnectionString, "Sharing:ConnectionString");

    if (!string.IsNullOrWhiteSpace(options.DatabasePath))
        return null;

    var controlPlane = configuration.GetConnectionString("ControlPlane");
    if (!string.IsNullOrWhiteSpace(controlPlane))
        return new SqlConnectionChoice(controlPlane, "ConnectionStrings:ControlPlane");

    return null;
}

static IResult Lookup<T>(string code, Func<string, string?, ShareLookup<T>> read, string? machine)
{
    if (!TryCanonical(code, out var canonical, out var error))
        return error!;
    var lookup = read(canonical, machine);
    if (lookup.Status != ShareStatus.Available || lookup.Value == null)
        return StatusFor(lookup.Status, lookup.Error);
    return Results.Ok(lookup.Value);
}

static bool TryCanonical(string code, out string canonical, out IResult? error)
{
    canonical = ShareCodes.Normalize(code);
    if (!ShareCodes.IsWellFormed(canonical))
    {
        error = Results.BadRequest(new ShareApiError { Error = "Enter the 8-character share code." });
        return false;
    }
    error = null;
    return true;
}

static IResult StatusFor(ShareStatus status, string? message) => status switch
{
    ShareStatus.NotFound => Results.NotFound(new ShareApiError { Error = message ?? "No share matches that code." }),
    ShareStatus.Expired or ShareStatus.Revoked => Results.Json(new ShareApiError { Error = message ?? "That share is no longer available." }, statusCode: StatusCodes.Status410Gone),
    _ => Results.BadRequest(new ShareApiError { Error = message ?? "The sharing server rejected the request." })
};

file sealed record SqlConnectionChoice(string Value, string Source);

public partial class Program;
