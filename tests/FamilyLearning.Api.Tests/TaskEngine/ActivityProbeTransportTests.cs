using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FamilyLearning.Evaluation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ActivityProbeTransportTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"activity-probe-{Guid.NewGuid():N}");

    [Fact]
    public async Task Real_adapter_uses_guarded_native_schema_and_combined_output_cap()
    {
        var output = """{"id":"isolated","model":"openai/gpt-6.1-sol","created":0,"choices":[{"index":0,"message":{"role":"assistant","content":"{\"result\":{\"proposal\":null,\"clarification\":\"לאיזה גיל?\"},\"assumptions\":[]}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":20,"completion_tokens":30,"total_tokens":50,"cost":0.01}}""";
        var upstream = new StubHandler(output);
        using var guard = new ActivityProbeTransport(1m, directory, upstream);
        using var http = new HttpClient(guard);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "TEST API KEY",
            ["Ai:Model"] = "openai/gpt-6.1-sol",
            ["Ai:ResponseFormat"] = "json_schema",
            ["Ai:ReasoningEffort"] = "medium",
            ["Ai:MaxOutputTokens"] = "16384"
        }).Build();
        using var clients = EvaluationClients.Create(configuration, new HostingEnvironment(), transport: new HttpClientPipelineTransport(http))!;
        using var service = ContentGenerationTests.Service(clients.Client, clients.Options);
        var reply = await service.AuthorAsync(new("פעילות"), default);
        Assert.Equal("לאיזה גיל?", reply.Value.Clarification);
        Assert.Equal(16384, upstream.Body!["max_completion_tokens"]!.GetValue<int>());
        Assert.Single(guard.Calls);
    }

    [Fact]
    public async Task Reserves_before_sending_and_keeps_unknown_cost_reserved()
    {
        var upstream = new StubHandler("""{"choices":[],"usage":{}}""");
        using var guard = new ActivityProbeTransport(0.35m, directory, upstream);
        using var client = new HttpClient(guard);
        using var first = await client.SendAsync(Request());
        using var second = await client.SendAsync(Request());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(Request()));
        Assert.Equal(2, upstream.Calls);
        Assert.Equal(2, guard.Calls.Count);
        Assert.All(guard.Calls, call => Assert.Null(call.CostUsd));
        Assert.True(guard.AccountedUsd > 0.33m);
    }

    [Fact]
    public async Task Pins_native_strict_request_routing_and_omits_secrets_reasoning_and_error_bodies_from_audit()
    {
        var upstream = new StubHandler("""{"id":"response-1","model":"openai/gpt-6.1-sol","choices":[{"message":{"content":"{}","reasoning":"PRIVATE THINKING"},"finish_reason":"stop"}],"usage":{"cost":0.01}}""");
        using var guard = new ActivityProbeTransport(1m, directory, upstream);
        using var client = new HttpClient(guard);
        using var response = await client.SendAsync(Request());
        Assert.Equal("openai", upstream.Body!["provider"]!["only"]![0]!.GetValue<string>());
        Assert.False(upstream.Body["provider"]!["allow_fallbacks"]!.GetValue<bool>());
        Assert.Equal(2m, upstream.Body["provider"]!["max_price"]!["prompt"]!.GetValue<decimal>());
        Assert.Equal(10m, upstream.Body["provider"]!["max_price"]!["completion"]!.GetValue<decimal>());
        Assert.True(upstream.Body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>());
        Assert.Equal(0.01m, guard.AccountedUsd);
        var audit = await File.ReadAllTextAsync(Path.Combine(directory, "transport.json"));
        Assert.DoesNotContain("TEST API KEY", audit);
        Assert.DoesNotContain("PRIVATE THINKING", audit);
        Assert.Contains("response-1", audit);
    }

    [Fact]
    public async Task Gemini_profile_routes_only_to_google_at_its_own_price_caps_and_rejects_other_models()
    {
        var upstream = new StubHandler("""{"choices":[],"usage":{"cost":0.001}}""");
        using var guard = new ActivityProbeTransport(1m, directory, upstream, ActivityProbeModel.Gemini);
        using var client = new HttpClient(guard);
        using var response = await client.SendAsync(Request("google/gemini-3.8-flash"));
        var provider = upstream.Body!["provider"]!;
        Assert.Equal(["google-ai-studio", "google-vertex"], provider["only"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.Equal(0.75m, provider["max_price"]!["prompt"]!.GetValue<decimal>());
        Assert.Equal(3.75m, provider["max_price"]!["completion"]!.GetValue<decimal>());
        Assert.True(guard.Calls[0].ReservedUsd < ActivityProbeModel.Sol.Reserve(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(Request()));
        Assert.Equal("profile-mismatch", guard.StopReason);
        Assert.Equal(1, upstream.Calls);
    }

    [Fact]
    public async Task Provider_error_stops_future_calls_without_retry_or_recording_raw_error()
    {
        var upstream = new StubHandler("""{"error":{"message":"PRIVATE PROVIDER ERROR","code":"invalid_json_schema"}}""", HttpStatusCode.BadRequest);
        using var guard = new ActivityProbeTransport(1m, directory, upstream);
        using var client = new HttpClient(guard);
        using var response = await client.SendAsync(Request());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(Request()));
        Assert.Equal(1, upstream.Calls);
        var audit = await File.ReadAllTextAsync(Path.Combine(directory, "transport.json"));
        Assert.DoesNotContain("PRIVATE PROVIDER ERROR", audit);
        Assert.Contains("invalid_json_schema", audit);
    }

    [Fact]
    public async Task Eighteenth_call_is_blocked_even_when_reported_costs_are_zero()
    {
        var upstream = new StubHandler("""{"choices":[],"usage":{"cost":0}}""");
        using var guard = new ActivityProbeTransport(1m, directory, upstream);
        using var client = new HttpClient(guard);
        for (var index = 0; index < 17; index++) { using var response = await client.SendAsync(Request()); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(Request()));
        Assert.Equal(17, upstream.Calls);
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"invalid_json_schema\"}}")]
    [InlineData("{\"choices\":[],\"usage\":{\"cost\":-1}}")]
    public async Task Embedded_error_or_invalid_cost_stops_even_with_HTTP_200(string output)
    {
        var upstream = new StubHandler(output);
        using var guard = new ActivityProbeTransport(1m, directory, upstream);
        using var client = new HttpClient(guard);
        using var response = await client.SendAsync(Request());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(Request()));
        Assert.Single(guard.Calls);
        Assert.Null(guard.Calls[0].CostUsd);
        Assert.True(guard.AccountedUsd > 0);
    }

    private static HttpRequestMessage Request(string model = "openai/gpt-6.1-sol")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent("""{"model":"MODEL","max_tokens":16384,"reasoning":{"effort":"medium","exclude":true},"messages":[],"provider":{"require_parameters":true},"response_format":{"type":"json_schema","json_schema":{"strict":true,"name":"probe","schema":{"type":"object"}}}}""".Replace("MODEL", model), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer TEST API KEY");
        return request;
    }

    private sealed class StubHandler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        internal JsonNode? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            return new(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
