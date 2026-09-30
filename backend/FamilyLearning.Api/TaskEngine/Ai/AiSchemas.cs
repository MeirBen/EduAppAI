using System.Text.Json;

namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Application-owned JSON schemas shared by prompts and provider output options.</summary>
internal static class AiSchemas
{
    public static readonly JsonElement Template = Read("template.schema.json");
    private static readonly JsonElement Content = Read("content.schema.json");

    /// <summary>Constrains generation to the validated input count without changing the shared schema.</summary>
    public static JsonElement ContentFor(int questionCount)
    {
        var schema = JsonSerializer.SerializeToNode(Content)!;
        var questions = schema["properties"]!["questions"]!;
        questions["minItems"] = questionCount;
        questions["maxItems"] = questionCount;
        return JsonSerializer.SerializeToElement(schema);
    }

    private static JsonElement Read(string name)
    {
        using var stream = typeof(AiSchemas).Assembly.GetManifestResourceStream($"FamilyLearning.Api.TaskEngine.Ai.{name}")
            ?? throw new InvalidOperationException($"Missing AI schema: {name}");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
