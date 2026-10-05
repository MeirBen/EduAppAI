using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Application-owned JSON schemas for model output. Files hold structure; engine constants and request counts are applied here.</summary>
internal static class AiSchemas
{
    public static readonly JsonElement Template = LoadTemplate();
    public static readonly JsonElement Ideas = LoadIdeas();
    private static readonly JsonElement Materials = Read("materials.schema.json");
    private static readonly JsonElement Questions = Read("questions.schema.json");

    public static JsonElement MaterialsFor(string[] ids, bool replacement = false)
    {
        var schema = JsonSerializer.SerializeToNode(Materials)!;
        var materials = schema["properties"]!["materials"]!;
        materials["minItems"] = ids.Length;
        materials["maxItems"] = ids.Length;
        if (ids.Length > 0) materials["items"]!["properties"]!["id"]!["enum"] = JsonSerializer.SerializeToNode(ids);
        if (replacement) return JsonSerializer.SerializeToElement(materials["items"]);
        return JsonSerializer.SerializeToElement(schema);
    }

    private static JsonElement LoadIdeas()
    {
        var schema = JsonSerializer.SerializeToNode(Read("material-ideas.schema.json"))!;
        var ideas = schema["properties"]!["ideas"]!;
        ideas["minItems"] = MaterialIdeas.CandidateCount;
        ideas["maxItems"] = MaterialIdeas.CandidateCount;
        var idea = ideas["items"]!["properties"]!["idea"]!["properties"]!;
        idea["premise"]!["maxLength"] = MaterialIdeas.TextLimit;
        idea["structure"]!["maxLength"] = MaterialIdeas.TextLimit;
        return JsonSerializer.SerializeToElement(schema);
    }

    public static JsonElement QuestionsFor(ResolvedTaskRequest request, int exactCountLimit, bool replacement = false)
    {
        var schema = JsonSerializer.SerializeToNode(Questions)!;
        var questions = schema["properties"]!["questions"]!;
        // Above the endpoint's limit an exact-count array exceeds its schema budget; the prompt and validator own the count.
        if (request.Settings.QuestionCount <= exactCountLimit)
        {
            questions["minItems"] = request.Settings.QuestionCount;
            questions["maxItems"] = request.Settings.QuestionCount;
        }
        var interaction = questions["items"]!["properties"]!["interaction"]!["properties"]!;
        interaction["type"]!["enum"] = JsonSerializer.SerializeToNode(request.Questions.Formats);
        if (request.Questions.ChoiceCount is { } count)
        {
            interaction["options"]!["minItems"] = count;
            interaction["options"]!["maxItems"] = count;
        }
        // ["null"] is the portable null-only form; OpenRouter widens a standalone null type to a nullable string for Gemini.
        else interaction["options"] = new JsonObject { ["type"] = new JsonArray("null") };
        if (request.Questions.Formats is ["single-choice"]) interaction["options"]!["type"] = "array";
        return JsonSerializer.SerializeToElement(replacement ? questions["items"] : schema);
    }

    private static JsonElement LoadTemplate()
    {
        var schema = JsonSerializer.SerializeToNode(Read("template.schema.json"))!;
        var definitions = schema["$defs"]!;
        var plan = definitions["plan"]!["properties"]!;
        // Equal bounds pin the version portably; integer enums are not universally supported (OpenRouter erases this plan for Gemini).
        plan["schemaVersion"]!["minimum"] = EngineVersions.SchemaVersion;
        plan["schemaVersion"]!["maximum"] = EngineVersions.SchemaVersion;
        plan["defaults"]!["properties"]!["questionCount"]!["maximum"] = EngineValidation.MaxQuestionCount;
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
