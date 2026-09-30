using System.Text.Json;
using FamilyLearning.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace FamilyLearning.Api.Tests.Fixtures;

/// <summary>Loopback SDK wire fixture with isolated credentials; never loads application secrets.</summary>
internal sealed class LocalAiProvider : IAsyncDisposable
{
    private readonly WebApplication server;
    internal List<byte[]> Bodies { get; } = [];
    internal Func<JsonElement, string> Respond { get; set; } = _ => "{}";

    private LocalAiProvider()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        server = builder.Build();
        server.MapPost("/chat/completions", async (HttpRequest request) =>
        {
            using var stream = new MemoryStream();
            await request.Body.CopyToAsync(stream);
            var bytes = stream.ToArray();
            lock (Bodies) Bodies.Add(bytes);
            using var json = JsonDocument.Parse(bytes);
            return Results.Json(new
            {
                id = "isolated",
                model = "test/free",
                created = 0,
                choices = new[] { new { index = 0, message = new { role = "assistant", content = Respond(json.RootElement) }, finish_reason = "stop" } }
            });
        });
    }

    internal static async Task<LocalAiProvider> StartAsync()
    {
        var provider = new LocalAiProvider();
        await provider.server.StartAsync();
        return provider;
    }

    internal ServiceProvider Services(string mode = "json_schema", Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-key",
            ["Ai:Model"] = "test/free",
            ["Ai:Endpoint"] = server.Urls.Single(),
            ["Ai:ResponseFormat"] = mode
        };
        if (overrides is not null) foreach (var pair in overrides) settings[pair.Key] = pair.Value;
        var services = new ServiceCollection().AddLogging();
        services.AddTaskAi(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new HostingEnvironment { EnvironmentName = "Development" });
        return services.BuildServiceProvider();
    }

    public ValueTask DisposeAsync() => server.DisposeAsync();
}
