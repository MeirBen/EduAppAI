using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

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
        if (!replacement) RequireQuestionOutputCapacity(request);
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
        else interaction["options"] = new JsonObject { ["type"] = "null" };
        if (request.Questions.Formats is ["single-choice"]) interaction["options"]!["type"] = "array";
        return JsonSerializer.SerializeToElement(replacement ? questions["items"] : schema);
    }

    internal static void RequireQuestionOutputCapacity(ResolvedTaskRequest request)
    {
        // Even minimal valid JSON must fit the response ceiling. This is a serialization bound,
        // not another educational question-count limit; no count-sized allocation is needed.
        var sizes = request.Questions.Formats.Select(format => JsonSerializer.Serialize(new QuestionCandidate("a",
            new(format, format == "single-choice" ? Enumerable.Range(0, request.Questions.ChoiceCount!.Value).Select(i => i.ToString()).ToArray() : null),
            new("0"), 0), EngineJson.Options).Length).ToArray();
        var framing = JsonSerializer.Serialize(new QuestionCandidateBatch("a", "", []), EngineJson.Options).Length;
        var minimum = framing + sizes.Sum() + (long)(request.Settings.QuestionCount - sizes.Length) * sizes.Min() + request.Settings.QuestionCount - 1;
        if (minimum > AiGenerationOptions.OutputCharacterLimit)
            throw new TaskValidationException(new Dictionary<string, string[]> { ["questions"] = ["מספר השאלות אינו יכול להיכנס למגבלת פלט ה־AI."] });
    }

    private static JsonElement LoadTemplate()
    {
        var schema = JsonSerializer.SerializeToNode(Read("template.schema.json"))!;
        schema["$defs"]!["plan"]!["properties"]!["schemaVersion"]!["enum"] = new JsonArray(EngineVersions.SchemaVersion);
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
