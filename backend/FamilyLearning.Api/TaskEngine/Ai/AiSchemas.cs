using System.Text.Json;

namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Strict provider schemas owned by the application, independently checked by domain validators.</summary>
internal static class AiSchemas
{
    public static readonly JsonElement Template = Read("template.schema.json");
    public static readonly JsonElement Content = Read("content.schema.json");

    private static JsonElement Read(string name)
    {
        using var stream = typeof(AiSchemas).Assembly.GetManifestResourceStream($"FamilyLearning.Api.TaskEngine.Ai.{name}")
            ?? throw new InvalidOperationException($"Missing AI schema: {name}");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
