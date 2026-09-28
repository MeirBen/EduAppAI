using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FamilyLearning.Api.Features.Auth;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Features.Templates;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Infrastructure.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var management = args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal) &&
    args[0] is "--migrate" or "--create-parent";
var builder = WebApplication.CreateBuilder(management ? [] : args);
var dataDirectory = Path.GetFullPath(builder.Configuration["Storage:Directory"] ?? "data", builder.Environment.ContentRootPath);
if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dataDirectory);
else Directory.CreateDirectory(dataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
builder.Services.AddDbContext<LearningDbContext>(options => options.UseSqlite(
    new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(dataDirectory, "family-learning.db"),
        ForeignKeys = true,
        Pooling = false
    }.ToString()));
builder.Services.AddDataProtection().SetApplicationName("FamilyLearning")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
builder.Services.AddParentAuthentication(builder.Environment.IsDevelopment());
// Keep malformed JSON a 400 response in Development as well as Production.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
var app = builder.Build();
if (management)
{
    Environment.ExitCode = await ManagementCommand.RunAsync(app.Services, args);
    return;
}
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<LearningDbContext>().Database.MigrateAsync();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
var api = app.MapGroup("/api").AddEndpointFilter<CsrfFilter>();
api.AddEndpointFilter(async (context, next) =>
{
    context.HttpContext.Response.Headers.CacheControl = "no-store";
    return await next(context);
});
api.MapAuthEndpoints();
api.MapTemplateEndpoints();
api.MapInstanceEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Map("/api/{**path}", () => Results.NotFound());
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
