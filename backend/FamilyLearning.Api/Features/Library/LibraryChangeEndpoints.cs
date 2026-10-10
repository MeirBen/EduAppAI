using FamilyLearning.Api.Infrastructure.Auth;

namespace FamilyLearning.Api.Features.Library;

/// <summary>Server-sent change notes for the caller's family, and the route convention that publishes them.</summary>
public static class LibraryChangeEndpoints
{
    private static readonly ReadOnlyMemory<byte> Note = "data: changed\n\n"u8.ToArray();

    public static void MapLibraryChangeEndpoints(this RouteGroupBuilder api, string path = "/library/changes") => api.MapGet(path, StreamAsync);

    /// <summary>Notifies the caller's family after any successful write in this group.</summary>
    /// <remarks>Feature handlers commit before returning success, so a 2xx result follows the commit; failures publish nothing.</remarks>
    public static RouteGroupBuilder PublishesLibraryChanges(this RouteGroupBuilder routes) =>
        routes.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            var http = context.HttpContext;
            if (!HttpMethods.IsGet(http.Request.Method) && result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
                http.RequestServices.GetRequiredService<LibraryChanges>().Publish(http.User.FamilyId());
            return result;
        });

    private static async Task<IResult> StreamAsync(HttpContext context, LibraryChanges changes, IHostApplicationLifetime host)
    {
        var familyId = context.User.FamilyId();
        var stream = changes.Subscribe(familyId);
        if (stream is null) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        // Shutdown must not wait for open streams; clients reconnect on their own.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, host.ApplicationStopping);
        lifetime.CancelAfter(LibraryChanges.StreamLifetime);
        context.Response.ContentType = "text/event-stream";
        try
        {
            await foreach (var _ in stream.Reader.ReadAllAsync(lifetime.Token))
                await context.Response.Body.WriteAsync(Note, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { changes.Unsubscribe(familyId, stream); }
        return Results.Empty;
    }
}
