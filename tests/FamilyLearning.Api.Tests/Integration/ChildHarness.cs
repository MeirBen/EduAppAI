using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyLearning.Api.Tests.Integration;

internal sealed class ChildHarness : IAsyncDisposable
{
    internal GenerationHarness.TestClock Clock { get; } = new();
    internal ApiFactory App { get; }
    internal AiFixtures.ScriptedChat Chat { get; } = new();

    internal ChildHarness(Action<IServiceCollection>? configure = null)
    {
        App = new ApiFactory(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IChatClient>(Chat);
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme,
                options => options.TimeProvider = TimeProvider.System);
            configure?.Invoke(services);
        });
    }

    internal static async Task<JsonNode> Create(HttpClient parent, string name = "ילד")
    {
        using var response = await parent.PostAsJsonAsync("/api/children", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    internal static string Path(JsonNode child) => $"/api/children/{child["id"]!.GetValue<Guid>()}";

    internal static async Task<JsonNode> Issue(HttpClient parent, JsonNode child, string deviceLabel = "מחשב")
    {
        using var response = await parent.PostAsJsonAsync(Path(child) + "/activation", new { deviceLabel });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    internal static async Task<string> Csrf(HttpClient client)
    {
        var result = (await client.GetFromJsonAsync<JsonNode>("/api/child/auth/csrf"))!;
        var token = result["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
        return token;
    }

    internal async Task<HttpClient> Activate(HttpClient parent, JsonNode profile)
    {
        var code = await Issue(parent, profile);
        var client = App.CreateClient(new() { AllowAutoRedirect = false });
        await Csrf(client);
        using var response = await client.PostAsJsonAsync("/api/child/auth/activate", new { code = code["code"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await Csrf(client);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        Assert.Empty(Chat.Requests);
    }
}
