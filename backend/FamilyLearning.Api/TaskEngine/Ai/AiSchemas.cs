using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Application-owned JSON schemas shared by prompts and provider output options.</summary>
internal static class AiSchemas
{
    public static readonly JsonElement Blueprint = Read("blueprint.schema.json");
    public static readonly JsonElement Template = LoadTemplate();
    private static readonly JsonElement Content = Read("content.schema.json");
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

    internal static void RequireQuestionOutputCapacity(ResolvedTaskRequest request, int additionalCharacters = 0)
    {
        // Even minimal valid JSON must fit the response ceiling. This is a serialization bound,
        // not another educational question-count limit; no count-sized allocation is needed.
        var sizes = request.Questions.Formats.Select(format => JsonSerializer.Serialize(new QuestionCandidate("a",
            new(format, format == "single-choice" ? Enumerable.Range(0, request.Questions.ChoiceCount!.Value).Select(i => i.ToString()).ToArray() : null),
            new("0"), 0), EngineJson.Options).Length).ToArray();
        var framing = JsonSerializer.Serialize(new QuestionCandidateBatch("a", "", []), EngineJson.Options).Length;
        var minimum = additionalCharacters + framing + sizes.Sum() + (long)(request.Settings.QuestionCount - sizes.Length) * sizes.Min() + request.Settings.QuestionCount - 1;
        if (minimum > AiGenerationOptions.OutputCharacterLimit)
            throw new TaskValidationException(new Dictionary<string, string[]> { ["questions"] = ["מספר השאלות אינו יכול להיכנס למגבלת פלט ה־AI."] });
    }

    // Temporary matched experiment: compose the same definitions; no second set of content rules.
    public static JsonElement OneShotFor(ResolvedTaskRequest request)
    {
        // Generated bodies are bounded separately; count their smallest legal representation in the combined envelope too.
        var generated = request.Materials.Where(m => m.Source == "generated").ToArray();
        var minimumBodies = generated.Select(m => new MaterialCandidate(m.Id, "", new string('a',
            m.Length?.Mode == "exact" ? checked(2 * m.Length.Value!.Value - 1) :
            m.Length?.Mode == "range" ? checked(2 * m.Length.Lower!.Value - 1) : 1))).ToArray();
        var materialCharacters = JsonSerializer.Serialize(new MaterialCandidateBatch(minimumBodies), EngineJson.Options).Length - 1;
        if (request.TotalLength is { Mode: "exact" or "range" } total)
            materialCharacters += checked(2 * (total.Value ?? total.Lower)!.Value - 2 * generated.Length);
        RequireQuestionOutputCapacity(request, materialCharacters);
        var schema = JsonSerializer.SerializeToNode(QuestionsFor(request))!;
        var materials = MaterialsFor(request.Materials.Where(m => m.Source == "generated").Select(m => m.Id).ToArray());
        schema["properties"]!["materials"] = JsonSerializer.SerializeToNode(materials.GetProperty("properties").GetProperty("materials"));
        schema["required"]!.AsArray().Add("materials");
        return JsonSerializer.SerializeToElement(schema);
    }

    private static JsonElement LoadTemplate()
    {
        var schema = JsonSerializer.SerializeToNode(Read("template.schema.json"))!;
        schema["$defs"]!["plan"]!["properties"]!["schemaVersion"]!["enum"] = new JsonArray(EngineVersions.SchemaVersion);
        return JsonSerializer.SerializeToElement(schema);
    }

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
