using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FamilyLearning.Api.Features.Ai;
using FamilyLearning.Api.Features.Auth;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Features.Templates;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyLearning.Api.Infrastructure.Web;

/// <summary>Shared API contracts and policies; feature endpoints inherit parent authorization and CSRF protection.</summary>
public static class ApiConfiguration
{
    /// <summary>Registers strict JSON, safe problem responses and per-process request limits.</summary>
    public static IServiceCollection AddApplicationApi(this IServiceCollection services)
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
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("generation", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst("family_id")?.Value ?? "anonymous", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }

    /// <summary>Maps parent APIs; only sign-in and token issuance explicitly allow anonymous access.</summary>
    public static void MapApplicationApi(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization("Parent").AddEndpointFilter<CsrfFilter>();
        api.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (AiGenerationException exception)
            { return Results.Problem(statusCode: exception.StatusCode, title: exception.Message, type: exception.ProblemType); }
        });
        api.MapAuthEndpoints();
        api.MapAiEndpoints();
        api.MapTemplateEndpoints();
        api.MapInstanceEndpoints();
        // An unknown API route must remain a 404 instead of returning Angular's HTML fallback.
        app.Map("/api/{**path}", () => Results.NotFound());
    }
}
