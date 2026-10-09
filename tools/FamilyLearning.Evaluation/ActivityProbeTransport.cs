using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FamilyLearning.Evaluation;

/// <summary>Opt-in probe transport; production never registers this handler.</summary>
internal sealed class ActivityProbeTransport(decimal budgetUsd, string directory, HttpMessageHandler inner, ActivityProbeModel? profile = null)
    : DelegatingHandler(inner)
{
    private readonly ActivityProbeModel model = profile ?? ActivityProbeModel.Sol;
    internal const int MaxCalls = 17;
    internal const int MaxRequestBytes = 65_536;
    internal const int MaxOutputTokens = 16_384;
    internal List<ActivityProbeCall> Calls { get; } = [];
    internal decimal AccountedUsd => Calls.Sum(call => call.CostUsd ?? call.ReservedUsd);
    internal string? StopReason { get; private set; }
    private bool stopped;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (stopped || Calls.Count >= MaxCalls) throw Reject(StopReason ?? "call-limit");
        if (budgetUsd is <= 0 or > 6 || request.Method != HttpMethod.Post ||
            request.RequestUri?.AbsoluteUri != "https://openrouter.ai/api/v1/chat/completions")
            throw Reject("invalid-budget-or-endpoint");
        var original = await request.Content!.ReadAsByteArrayAsync(ct);
        if (original.Length > MaxRequestBytes) throw Reject("request-limit");
        var body = JsonNode.Parse(original)!;
        if (body["model"]?.GetValue<string>() != model.Model ||
            (body["max_completion_tokens"] ?? body["max_tokens"])?.GetValue<int>() != MaxOutputTokens ||
            body["max_completion_tokens"] is not null && body["max_tokens"] is not null ||
            body["response_format"]?["type"]?.GetValue<string>() != "json_schema" ||
            body["response_format"]?["json_schema"]?["strict"]?.GetValue<bool>() != true ||
            body["reasoning"]?["effort"]?.GetValue<string>() != "medium" || body["reasoning"]?["exclude"]?.GetValue<bool>() != true ||
            body["models"] is not null || body["tools"] is not null || body["stream"]?.GetValue<bool>() == true)
            throw Reject("profile-mismatch");
        body["provider"] = new JsonObject
        {
            ["only"] = new JsonArray(model.Providers.Select(provider => (JsonNode)JsonValue.Create(provider)!).ToArray()),
            ["allow_fallbacks"] = false,
            ["require_parameters"] = true,
            ["max_price"] = new JsonObject { ["prompt"] = model.PromptPerMillion, ["completion"] = model.CompletionPerMillion }
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        if (bytes.Length > MaxRequestBytes) throw Reject("request-limit");
        var reserve = model.Reserve(bytes.Length);
        if (AccountedUsd + reserve > budgetUsd) throw Reject("budget-limit");
        var call = new ActivityProbeCall(reserve, body, Convert.ToHexString(SHA256.HashData(original)), Convert.ToHexString(SHA256.HashData(bytes)));
        Calls.Add(call);
        await SaveAsync();
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new("application/json");
        try
        {
            var response = await base.SendAsync(request, ct);
            call.StatusCode = (int)response.StatusCode;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("cost", out var cost) && cost.ValueKind != JsonValueKind.Null)
                {
                    if (cost.ValueKind == JsonValueKind.Number && cost.TryGetDecimal(out var value) && value >= 0) call.CostUsd = value;
                    else call.ErrorCode = "invalid-cost";
                }
                call.InputTokens = Number(usage, "prompt_tokens");
                call.OutputTokens = Number(usage, "completion_tokens");
            }
            call.ResponseId = Text(root, "id", 256);
            call.Model = Text(root, "model", 256);
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                call.FinishReason = Text(choice, "finish_reason", 256);
                if (choice.TryGetProperty("message", out var message)) call.Output = Text(message, "content", 32_000);
            }
            if (root.TryGetProperty("error", out var error))
            {
                var code = Text(error, "code", 64);
                call.ErrorCode = code?.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') == true ? code : "provider-error";
            }
            stopped = !response.IsSuccessStatusCode || call.ErrorCode is not null || call.CostUsd > reserve;
            if (stopped) StopReason = call.ErrorCode ?? (!response.IsSuccessStatusCode ? "provider-error" : "cost-exceeded-reserve");
            return response;
        }
        catch { stopped = true; StopReason = call.ErrorCode ??= "transport-or-response-error"; throw; }
        finally { call.FinishedAtUtc = DateTime.UtcNow; await SaveAsync(); }
    }

    private InvalidOperationException Reject(string reason) { stopped = true; StopReason = reason; return new(reason); }

    private async Task SaveAsync()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "transport.json");
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new { budgetUsd, accountedUsd = AccountedUsd, stopped, calls = Calls }, EvaluationFiles.Json));
        File.Move(path + ".tmp", path, true);
    }

    private static string? Text(JsonElement element, string name, int limit) => element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= limit ? text : null;
    private static long? Number(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) && number >= 0 ? number : null;
}

/// <summary>A fixed probe model: exact ID, allowed providers and per-million price caps used for routing and reservations.</summary>
internal sealed record ActivityProbeModel(string Name, string Model, string[] Providers, decimal PromptPerMillion, decimal CompletionPerMillion)
{
    internal static readonly ActivityProbeModel Sol = new("sol", "openai/gpt-6.1-sol", ["openai"], 2m, 10m);
    // Standard Google tiers on OpenRouter; priority tiers exceed these caps.
    internal static readonly ActivityProbeModel Gemini = new("gemini", "google/gemini-3.8-flash", ["google-ai-studio", "google-vertex"], 0.75m, 3.75m);

    internal static ActivityProbeModel Select(string name) => name switch
    {
        "sol" => Sol,
        "gemini" => Gemini,
        _ => throw new ArgumentException("Unknown probe model.")
    };

    /// <summary>UTF-8 bytes conservatively bound text tokens; include schema/envelope overhead and all reasoning/output.</summary>
    internal decimal Reserve(int requestBytes) =>
        ((requestBytes + 4096) * PromptPerMillion + ActivityProbeTransport.MaxOutputTokens * CompletionPerMillion) / 1_000_000m;
}

internal sealed record ActivityProbeCall(decimal ReservedUsd, JsonNode Request, string OriginalRequestSha256, string RequestSha256)
{
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
    public decimal? CostUsd { get; set; }
    public int? StatusCode { get; set; }
    public string? ResponseId { get; set; }
    public string? Model { get; set; }
    public string? FinishReason { get; set; }
    public string? Output { get; set; }
    public string? ErrorCode { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
}
