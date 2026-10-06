using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Features.Ai;
using FamilyLearning.Api.Features.Assignments;
using FamilyLearning.Api.Features.Auth;
using FamilyLearning.Api.Features.Children;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Features.Library;
using FamilyLearning.Api.Features.Templates;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Validation;
using Serilog;

namespace FamilyLearning.Api.Infrastructure.Web;

/// <summary>Shared API contracts and policies; feature endpoints inherit explicit authorization and CSRF protection.</summary>
public static class ApiConfiguration
{
    /// <summary>Registers strict JSON, safe problem responses and per-process request limits.</summary>
    public static void AddApplicationApi(this IServiceCollection services)
    {
        // Keep malformed JSON a 400 response in Development as well as Production.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        services.AddSingleton<AiStartLimiter>();
        services.AddSingleton<LibraryChanges>();
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.GetEndpoint()?.Metadata.GetMetadata<DeviceSessionEntry>() is { RequireAnonymous: true }
                    ? RateLimitPartition.GetFixedWindowLimiter("child-redemption", _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("other"));
            options.AddPolicy("child-redemption", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("child-issuance", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    /// <summary>Maps sibling parent and child APIs with a shared failure/CSRF boundary.</summary>
    public static void MapApplicationApi(this WebApplication app)
    {
        var root = ApplicationApi(app);
        var api = root.MapGroup("").RequireAuthorization("Parent");
        api.MapAuthEndpoints();
        api.MapChildEndpoints();
        var child = root.MapGroup("/child").RequireAuthorization("Child");
        child.MapChildAuthEndpoints();
        child.MapChildAssignmentEndpoints();
        child.MapChildSessionEndpoints();
        api.MapGet("/limits", () => ContentLimits.Current);
        api.MapPlanAuthoringEndpoints();
        api.MapLibraryChangeEndpoints();
        // Successful writes to library content notify the family's open change streams.
        var library = api.MapGroup("").PublishesLibraryChanges();
        library.MapActivityEndpoints();
        library.MapGenerationOperationEndpoints();
        library.MapPlanTemplateEndpoints();
        library.MapSnapshotEndpoints();
        library.MapAssignmentEndpoints();
    }

    private static RouteGroupBuilder ApplicationApi(WebApplication app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<CsrfFilter>();
        api.AddEndpointFilter(async (context, next) =>
        {
            // Match antiforgery's cache policy so token issuance does not need to override it.
            context.HttpContext.Response.Headers.CacheControl = "no-cache, no-store";
            try { return await next(context); }
            catch (AiGenerationException exception)
            {
                context.HttpContext.RequestServices.GetRequiredService<IDiagnosticContext>().Set("Failure", exception.Category);
                return Results.Problem(statusCode: exception.StatusCode, title: exception.Message, type: exception.ProblemType,
                    extensions: exception.ValidationErrors is null ? null : new Dictionary<string, object?> { ["errors"] = exception.ValidationErrors });
            }
            catch (TaskValidationException exception)
            {
                return Results.ValidationProblem(exception.Errors.ToDictionary(pair => pair.Key, pair => pair.Value));
            }
        });
        // An unknown API route must remain a 404 instead of returning Angular's HTML fallback.
        app.Map("/api/{**path}", () => Results.NotFound());
        return api;
    }
}
