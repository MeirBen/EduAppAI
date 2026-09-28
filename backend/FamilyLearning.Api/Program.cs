using FamilyLearning.Api.Infrastructure.Ai;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Infrastructure.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var management = args.Length > 0 && args[0] is "--migrate" or "--create-parent";
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
// Persist keys with the database so restarts do not invalidate existing login cookies.
builder.Services.AddDataProtection().SetApplicationName("FamilyLearning")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
builder.Services.AddParentAuthentication(builder.Environment.IsDevelopment());
builder.Services.AddTaskAi(builder.Configuration, builder.Environment);
builder.Services.AddApplicationApi();
// Allow all bounded template fields even when Hebrew characters use six-byte JSON escapes.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);
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
app.MapApplicationApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    app.MapFallbackToFile("index.html");
app.Run();

/// <summary>Public host entry point exposed for in-process HTTP integration tests.</summary>
public partial class Program;
