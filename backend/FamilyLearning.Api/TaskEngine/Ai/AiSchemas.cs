using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Application-owned JSON schemas shared by prompts and provider output options.</summary>
internal static class AiSchemas
{
    public static readonly JsonElement Template = LoadTemplate();
    private static readonly JsonElement Materials = Read("materials.schema.json");
    private static readonly JsonElement Questions = Read("questions.schema.json");

    public static JsonElement MaterialsFor(string[] ids, bool replacement = false)
    {
        var schema = JsonSerializer.SerializeToNode(Materials)!;
        var materials = schema["properties"]!["materials"]!;
        materials["minItems"] = ids.Length;
        materials["maxItems"] = ids.Length;
        if (ids.Length > 0) materials["items"]!["properties"]!["id"]!["enum"] = JsonSerializer.SerializeToNode(ids);
        return JsonSerializer.SerializeToElement(replacement ? materials["items"] : schema);
    }

    public static JsonElement QuestionsFor(ResolvedTaskRequest request, bool replacement = false)
    {
        var schema = JsonSerializer.SerializeToNode(Questions)!;
        var questions = schema["properties"]!["questions"]!;
        questions["minItems"] = request.Settings.QuestionCount;
        questions["maxItems"] = request.Settings.QuestionCount;
        var interaction = questions["items"]!["properties"]!["interaction"]!["properties"]!;
        interaction["type"]!["enum"] = JsonSerializer.SerializeToNode(request.Questions.Formats);
        if (request.Questions.ChoiceCount is { } count)
        {
            interaction["options"]!["minItems"] = count;
            interaction["options"]!["maxItems"] = count;
        }
        // OpenRouter's Gemini conversion widens a standalone null type to a nullable string; ["null"] stays exact.
        else interaction["options"] = new JsonObject { ["type"] = new JsonArray("null") };
        if (request.Questions.Formats is ["single-choice"]) interaction["options"]!["type"] = "array";
        return JsonSerializer.SerializeToElement(replacement ? questions["items"] : schema);
    }

    private static JsonElement LoadTemplate()
    {
        var schema = JsonSerializer.SerializeToNode(Read("template.schema.json"))!;
        var version = schema["$defs"]!["plan"]!["properties"]!["schemaVersion"]!;
        // OpenRouter's Gemini conversion drops this plan with an integer enum; equal bounds preserve the exact version.
        version["minimum"] = EngineVersions.SchemaVersion;
        version["maximum"] = EngineVersions.SchemaVersion;
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
