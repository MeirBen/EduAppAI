using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyLearning.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyLearning.Evaluation;

/// <summary>Loopback-only developer UI. This host never composes the production API, identity or database.</summary>
public static class EvaluationDashboard
{
    public static async Task RunAsync(string[] args)
    {
        var port = 5180;
        var root = "artifacts/evaluations";
        var seen = new HashSet<string>();
        for (var index = 1; index < args.Length; index += 2)
        {
            if (!seen.Add(args[index]) || index + 1 == args.Length) throw new ArgumentException("Invalid dashboard arguments.");
            switch (args[index])
            {
                case "--port" when int.TryParse(args[index + 1], out var value) && value is >= 1 and <= 65535: port = value; break;
                case "--output" when !string.IsNullOrWhiteSpace(args[index + 1]): root = args[index + 1]; break;
                default: throw new ArgumentException("Use --ui [--port PORT] [--output DIR].");
            }
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(EvaluationDashboard).Assembly.GetName().Name
        });
        builder.Logging.ClearProviders();
        builder.Services.AddTaskAi(builder.Configuration, builder.Environment);
        await using var app = Build(builder, port, root);
        await app.StartAsync();
        Console.WriteLine($"Evaluation dashboard: http://127.0.0.1:{port} (no calls until a run is confirmed)");
        await app.WaitForShutdownAsync();
    }

    /// <summary>Composes local services after AI registration. Port zero is supported for isolated tests.</summary>
    public static WebApplication Build(WebApplicationBuilder builder, int port, string root)
    {
        builder.WebHost.ConfigureKestrel(server =>
        {
            // Replace configured endpoints too: ASPNETCORE_URLS/Kestrel settings must never expose this tool remotely.
            server.Configure(new ConfigurationBuilder().Build());
            server.Listen(IPAddress.Loopback, port);
            server.Limits.MaxRequestBodySize = 32 * 1024;
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.MaxDepth = 32;
        });
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-Evaluation-CSRF";
            options.Cookie.Name = "Evaluation-CSRF";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
        });
        builder.Services.AddSingleton(_ => new EvaluationRunStore(root));
        builder.Services.AddSingleton<EvaluationCoordinator>();
        builder.Services.AddHostedService(services => services.GetRequiredService<EvaluationCoordinator>());
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
            try
            {
                var origin = $"http://127.0.0.1:{context.Connection.LocalPort}";
                var suppliedOrigin = context.Request.Headers.Origin;
                var mutation = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
                if (context.Request.Host.Value != $"127.0.0.1:{context.Connection.LocalPort}" ||
                    context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
                    (suppliedOrigin.Count > 0 && suppliedOrigin != origin) || mutation && suppliedOrigin != origin ||
                    context.Request.Headers["Sec-Fetch-Site"] is var site && site.Count > 0 && site != "same-origin" && site != "none")
                {
                    await Results.Problem(statusCode: 400, title: "Only same-origin loopback requests are allowed.").ExecuteAsync(context);
                    return;
                }
                if (mutation) await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
                await next(context);
            }
            catch (Exception exception) when (!context.Response.HasStarted)
            {
                var (status, title) = exception switch
                {
                    AntiforgeryValidationException => (400, "Refresh the dashboard before submitting."),
                    ArgumentException or InvalidDataException or JsonException or BadHttpRequestException => (400, "Invalid request or evaluation data."),
                    FileNotFoundException or DirectoryNotFoundException => (404, "Run not found."),
                    InvalidOperationException => (409, "The operation is unavailable while a run is active or AI is unconfigured."),
                    _ => (500, "The evaluation operation failed. Check configuration and artifact permissions.")
                };
                await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
            }
        });
        app.MapGet("/api/setup", async (HttpContext context, IAntiforgery antiforgery, EvaluationCoordinator coordinator) =>
        {
            EvaluationCase[] cases = [];
            string? caseError = null;
            try { cases = (await EvaluationFiles.LoadFixtureAsync<EvaluationCase>("cases.json")).Items; }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            { caseError = "Evaluation cases are unavailable or invalid. Saved runs remain available."; }
            var calibrationCount = 0;
            string? judgeError = null;
            try
            {
                var controls = await EvaluationFiles.LoadFixtureAsync<CalibrationSample>("hebrew-review-samples.json");
                EvaluationFiles.ValidateCalibrationSamples(controls.Items);
                calibrationCount = controls.Items.Length;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            { judgeError = "Judge controls are unavailable or invalid. Basic evaluation is still available."; }
            return Results.Json(new
            {
                coordinator.Profile,
                coordinator.Configured,
                Cases = cases,
                CaseError = caseError,
                CalibrationCount = calibrationCount,
                JudgeAvailable = judgeError is null,
                JudgeError = judgeError,
                CsrfToken = antiforgery.GetAndStoreTokens(context).RequestToken
            });
        });
        app.MapGet("/api/active", (EvaluationCoordinator coordinator) => Results.Text(
            JsonSerializer.Serialize(coordinator.Active, EvaluationFiles.Json), "application/json"));
        app.MapGet("/api/runs", async (EvaluationRunStore store) => Results.Json(await store.ListAsync()));
        app.MapGet("/api/runs/{id}", async (string id, EvaluationRunStore store) =>
        {
            var report = await store.ReadAsync(id);
            return Results.Json(new { Id = id, Report = report, Summary = EvaluationSummary.Create(report) });
        });
        app.MapPost("/api/runs", async (EvaluationRunRequest request, EvaluationCoordinator coordinator) =>
        {
            var id = await coordinator.StartRunAsync(request);
            return Results.Accepted($"/api/runs/{id}", new { Id = id });
        });
        app.MapPost("/api/runs/{id}/cancel", (string id, EvaluationCoordinator coordinator) =>
        {
            coordinator.Cancel(id);
            return Results.Accepted(value: new { Status = "cancelling" });
        });
        app.MapPut("/api/runs/{id}/review", async (string id, EvaluationReviewUpdate request, EvaluationCoordinator coordinator) =>
        {
            await coordinator.SaveReviewAsync(id, request);
            return Results.NoContent();
        });
        app.MapGet("/api/compare", async (string baseline, string candidate, EvaluationRunStore store) =>
            Results.Json(EvaluationComparison.Compare(await store.ReadAsync(baseline), await store.ReadAsync(candidate))));
        foreach (var (route, file, type) in new[] { ("/", "index.html", "text/html"), ("/app.js", "app.js", "text/javascript"), ("/styles.css", "styles.css", "text/css") })
            app.MapGet(route, () => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", file), type));
        return app;
    }
}
