using System.Globalization;
using FamilyLearning.Api.TaskEngine.Ai;

namespace FamilyLearning.Api.Infrastructure.Ai;

/// <summary>Explicit nonsecret generation profile, shared by durable work and evaluation evidence.</summary>
public static class AiProfile
{
    public static Dictionary<string, string?> Capture(IConfiguration configuration, AiGenerationOptions options)
    {
        string[] keys = ["Model", "FallbackModel", "ResponseFormat", "ReasoningEnabled", "ReasoningEffort", "ReasoningMaxTokens", "Temperature", "TopP", "TopK"];
        var profile = keys.ToDictionary(key => key, key => configuration[$"Ai:{key}"]);
        profile["ResponseFormat"] ??= "json_object";
        profile["MaxOutputTokens"] = options.MaxOutputTokens.ToString(CultureInfo.InvariantCulture);
        profile["RequestTimeoutSeconds"] = options.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        profile["MaxRequestBytes"] = options.MaxRequestBytes.ToString(CultureInfo.InvariantCulture);
        profile["MaxSchemaBytes"] = options.MaxSchemaBytes.ToString(CultureInfo.InvariantCulture);
        return profile;
    }
}
