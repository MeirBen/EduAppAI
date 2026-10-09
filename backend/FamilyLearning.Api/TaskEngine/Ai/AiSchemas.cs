using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Application-owned JSON schemas for model output. Files hold structure; engine constants and request counts are applied here.</summary>
internal static class AiSchemas
{
    public static readonly JsonElement Authoring = LoadAuthoring();
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

    public static JsonElement QuestionsFor(ResolvedTaskRequest request, int exactCountLimit, bool replacement = false, int? outputCount = null)
    {
        var schema = JsonSerializer.SerializeToNode(Questions)!;
        var questions = schema["properties"]!["questions"]!;
        // Above the endpoint's limit an exact-count array exceeds its schema budget; the prompt and validator own the count.
        var countToReturn = outputCount ?? request.Settings.QuestionCount;
        if (countToReturn <= exactCountLimit)
        {
            questions["minItems"] = countToReturn;
            questions["maxItems"] = countToReturn;
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
        if (outputCount is not null)
        {
            schema["properties"]!.AsObject().Remove("title");
            schema["properties"]!.AsObject().Remove("instructions");
            schema["required"] = new JsonArray("questions");
        }
        return JsonSerializer.SerializeToElement(replacement ? questions["items"] : schema);
    }

    public static JsonElement RevisionFor(ActivityRevisionInput input)
    {
        var schema = JsonSerializer.SerializeToNode(Read("activity-revision.schema.json"))!;
        var definitions = schema["$defs"]!;
        foreach (var definition in JsonSerializer.SerializeToNode(Authoring)!["$defs"]!.AsObject())
            definitions[definition.Key] = definition.Value!.DeepClone();
        var branches = schema["properties"]!["result"]!["anyOf"]!;
        branches[0]!["properties"]!["answer"]!["maxLength"] = EngineValidation.RevisionReplyLength;
        branches[1]!["properties"]!["clarification"]!["maxLength"] = EngineValidation.RevisionReplyLength;
        var change = branches[2]!["properties"]!["change"]!["properties"]!;
        change["assumptions"]!["maxItems"] = EngineValidation.MaxAssumptions;
        change["assumptions"]!["items"]!["maxLength"] = EngineValidation.AssumptionLength;
        change["questions"]!["properties"]!["instruction"]!["maxLength"] = EngineValidation.MessageLength;
        ApplyTargets(change["materialEdits"]!, "materialEdit", input.Plan.Materials.Where(m => m.Source == "generated" && input.Current.Materials.Any(c => c.Id == m.Id)).Select(m => m.Id!).ToArray(), EngineValidation.MaxMaterials);
        var questionIds = input.Current.Questions.Select(q => q.Id).ToArray();
        ApplyTargets(change["questions"]!["properties"]!["items"]!, "questionEdit", questionIds, EngineValidation.MaxSelectedEdits);
        change["questionOrder"]!["maxItems"] = questionIds.Length;
        if (questionIds.Length > 0) change["questionOrder"]!["items"]!["enum"] = JsonSerializer.SerializeToNode(questionIds);
        return JsonSerializer.SerializeToElement(schema);

        void ApplyTargets(JsonNode array, string definition, string[] ids, int maximum)
        {
            array["maxItems"] = ids.Length == 0 ? 0 : maximum;
            definitions[definition]!["properties"]!["instruction"]!["minLength"] = 1;
            definitions[definition]!["properties"]!["instruction"]!["maxLength"] = EngineValidation.EditInstructionLength;
            if (ids.Length > 0) definitions[definition]!["properties"]!["id"]!["enum"] = JsonSerializer.SerializeToNode(ids);
        }
    }

    private static JsonElement LoadAuthoring()
    {
        var schema = JsonSerializer.SerializeToNode(Read("activity-authoring.schema.json"))!;
        var properties = schema["properties"]!;
        properties["result"]!["anyOf"]![1]!["properties"]!["clarification"]!["maxLength"] = EngineValidation.AuthoringReplyLength;
        properties["assumptions"]!["maxItems"] = EngineValidation.MaxAssumptions;
        properties["assumptions"]!["items"]!["maxLength"] = EngineValidation.AssumptionLength;
        var definitions = schema["$defs"]!;
        var plan = definitions["plan"]!["properties"]!;
        // Equal bounds pin the version portably; integer enums are not universally supported (OpenRouter erases this plan for Gemini).
        plan["schemaVersion"]!["minimum"] = EngineVersions.SchemaVersion;
        plan["schemaVersion"]!["maximum"] = EngineVersions.SchemaVersion;
        plan["settings"]!["properties"]!["questionCount"]!["maximum"] = EngineValidation.MaxQuestionCount;
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
