using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using Serilog.Events;

namespace FamilyLearning.Api.Infrastructure.Web;

/// <summary>Request completion levels, safe route metadata and correlation at the HTTP boundary.</summary>
public static class RequestLogging
{
    /// <summary>One completion event per request, including handled exceptions; excludes payloads, headers and query strings.</summary>
    public static void UseApplicationRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            // Each host owns its logger, including concurrent in-process test hosts.
            options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
            options.GetLevel = (context, _, exception) =>
            {
                if (context.RequestAborted.IsCancellationRequested && (exception is OperationCanceledException || context.Response.StatusCode == 499))
                    return LogEventLevel.Debug;
                if (exception is not null || context.Response.StatusCode >= 500) return LogEventLevel.Error;
                if (context.Response.StatusCode >= 400) return LogEventLevel.Warning;
                return !context.Request.Path.StartsWithSegments("/api") || HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
                    ? LogEventLevel.Debug : LogEventLevel.Information;
            };
            // Route templates keep arbitrary URL content out of logs, including unmatched paths.
            options.GetMessageTemplateProperties = (context, _, elapsed, statusCode) =>
            [
                new("RequestMethod", new ScalarValue(context.Request.Method)),
                new("RequestPath", new ScalarValue(((context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint) as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)")),
                new("StatusCode", new ScalarValue(statusCode)),
                new("Elapsed", new ScalarValue(elapsed))
            ];
            options.EnrichDiagnosticContext = (diagnostics, context) =>
            {
                diagnostics.Set("RequestId", context.TraceIdentifier);
                // The outer request logger owns exception reporting; the framework's duplicate event is suppressed.
                if (context.Features.Get<IExceptionHandlerFeature>() is { } failure)
                    diagnostics.SetException(failure.Error);
            };
        });
    }
}
