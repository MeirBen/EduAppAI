using System.Text.Json;

namespace FamilyLearning.Api.Infrastructure.Persistence;

// One explicit wire format for versioned JSON snapshots; do not serialize EF entities.
public static class StoredJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException("A stored snapshot cannot be null.");
}
