using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Infrastructure.Ai;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Logging;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Infrastructure.Web;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;

var management = args.Length > 0 && args[0] is "--migrate" or "--activity-only-cutover" or "--create-parent";
using var bootstrapLogger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
WebApplication? app = null;
try
{
    var builder = WebApplication.CreateBuilder(management ? [] : args);
    builder.AddApplicationLogging();
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
    if (management)
    {
        // Local database/account operations must not depend on a working AI profile or HTTP policies.
        app = builder.Build();
        Environment.ExitCode = await ManagementCommand.RunAsync(app.Services, args);
        app.Logger.LogInformation("Management command {Command} finished with exit code {ExitCode}", args[0], Environment.ExitCode);
        return;
    }
    builder.Services.AddTaskAi(builder.Configuration, builder.Environment);
    builder.Services.AddApplicationApi();
    builder.Services.AddActivityGeneration(builder.Configuration);
    // Allow bounded activity plans and imported chat even when Hebrew characters use six-byte JSON escapes.
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 3 * 1024 * 1024);
    app = builder.Build();
    app.Logger.LogInformation("Starting API host with AI configured: {AiConfigured}", app.Services.GetRequiredService<AiGenerationService>().Configured);
    // Local proxies terminate TLS; retain the framework's one-hop, loopback-only trust defaults.
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });
    app.UseApplicationRequestLogging();
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
    // Angular serves the UI separately during development; publishing supplies wwwroot.
    if (Directory.Exists(app.Environment.WebRootPath))
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
    }
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
    app.MapApplicationApi();
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
        app.MapFallbackToFile("index.html");
    await app.StartAsync();
    await app.WaitForShutdownAsync();
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    if (app is null) bootstrapLogger.Fatal(exception, "Host configuration failed");
    else app.Logger.LogCritical(exception, "Host terminated unexpectedly");
    throw;
}
finally
{
    // Dispose after failure reporting so the host-owned sinks can flush the final event.
    if (app is not null) await app.DisposeAsync();
}

/// <summary>Public host entry point exposed for in-process HTTP integration tests.</summary>
public partial class Program;
