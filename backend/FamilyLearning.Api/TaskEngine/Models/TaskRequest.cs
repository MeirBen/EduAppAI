using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>Complete settings with presence-preserving overrides. Undefined means omitted; explicit JSON null is invalid.</summary>
/// <remarks>JSON is confined to variable input values/maps. Resolution returns typed requirements and detached values.</remarks>
public sealed record TaskRequest(
    [property: JsonRequired] TaskSettings Settings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement QuestionFormat = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement ChoiceCount = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement MaterialInputs = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement TotalWordCount = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement ControlValues = default);

/// <summary>Either a complete effective request or field errors. A failed resolution never exposes partial values.</summary>
public sealed record TaskResolution(ResolvedTaskRequest? Value, Dictionary<string, string[]> Errors);

/// <summary>Effective requirements only, with defaults applied. Treat collections as immutable after resolution.</summary>
public sealed record ResolvedTaskRequest(int SchemaVersion, int EngineRevision, string Goal, string Guidance,
    TaskSettings Settings, ResolvedMaterial[] Materials, ResolvedQuestions Questions,
    ResolvedControl[] Controls, ResolvedLength? TotalLength);

/// <summary>One material's effective instructions and exact accepted source, where supplied.</summary>
public sealed record ResolvedMaterial(string Id, string Label, string Source, string Guidance, string? Text,
    ResolvedLength? Length, ResolvedControl[] Controls);

/// <summary>Every listed format must occur; a selected format resolves to a single-entry array.</summary>
public sealed record ResolvedQuestions(string[] Formats, int? ChoiceCount, string Guidance, ResolvedControl[] Controls);

/// <summary>A selected value travels with its scope and meaning, including the selected option's meaning.</summary>
public sealed record ResolvedControl(string Id, string Label, string Type, string Meaning, string? Unit,
    JsonElement Value, string? OptionMeaning);

/// <summary>A generated-word expectation: an advisory target Value, or a strict inclusive Lower–Upper range.</summary>
public sealed record ResolvedLength(string Mode, int? Value = null, int? Lower = null, int? Upper = null);
