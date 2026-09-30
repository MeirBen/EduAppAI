using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine;

/// <summary>One compact serializer for engine input, size checks and deterministic requirement fingerprints.</summary>
internal static class EngineJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict,
            MaxDepth = 16,
            // Engine JSON is data sent to a provider or stored, never embedded in HTML.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
