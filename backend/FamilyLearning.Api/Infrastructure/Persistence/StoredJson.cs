using System.Text.Json;

namespace FamilyLearning.Api.Infrastructure.Persistence;

/// <summary>One explicit JSON format for stored snapshots, separate from HTTP request validation.</summary>
/// <remarks>Serialize contract models, not EF entities. Reading never upgrades or rewrites a snapshot.</remarks>
public static class StoredJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    /// <summary>Serializes a snapshot with web defaults, including camel-case property names.</summary>
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    /// <summary>Deserializes a stored snapshot without regeneration or domain validation.</summary>
    /// <exception cref="JsonException">The JSON cannot be deserialized into the requested contract.</exception>
    /// <exception cref="InvalidOperationException">The document represents null.</exception>
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException("A stored snapshot cannot be null.");
}
