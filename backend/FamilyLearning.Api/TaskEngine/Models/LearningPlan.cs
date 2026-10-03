using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>Application-owned learning requirements. Validate before use; nested collections remain mutable.</summary>
/// <remarks>Proposals share this shape but may have null new IDs. Only normalized, validated plans are canonical.</remarks>
public sealed record LearningPlan(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Goal,
    [property: JsonRequired] string Guidance,
    [property: JsonRequired] TaskSettings Defaults,
    [property: JsonRequired] MaterialDefinition[] Materials,
    [property: JsonRequired] QuestionPlan Questions,
    [property: JsonRequired] ControlDefinition[] Controls,
    LengthExpectation? TotalLength = null,
    [property: JsonRequired] int SchemaVersion = EngineVersions.SchemaVersion);

/// <summary>A generated, verbatim fixed, or required per-task source. Only generated bodies have length requirements.</summary>
public sealed record MaterialDefinition(
    [property: JsonRequired] string? Id,
    [property: JsonRequired] string Label,
    [property: JsonRequired] string Source,
    [property: JsonRequired] string Guidance,
    string? Text,
    LengthExpectation? Length,
    [property: JsonRequired] ControlDefinition[] Controls);

/// <summary>Allowed question formats: a fixed mixture, or one parent-selected format for the whole task.</summary>
public sealed record QuestionPlan(
    [property: JsonRequired] string[] Formats,
    [property: JsonRequired] bool SelectableFormat,
    string? DefaultFormat,
    IntegerChoice? ChoiceCount,
    [property: JsonRequired] string Guidance,
    [property: JsonRequired] ControlDefinition[] Controls);

/// <summary>A positive count; adjustable values may change per activity within the application's limits.</summary>
public sealed record IntegerChoice([property: JsonRequired] int Value, [property: JsonRequired] bool Adjustable);

/// <summary>An approximate target uses Count; a strict inclusive range uses Lower below Upper instead.</summary>
public sealed record LengthExpectation([property: JsonRequired] string Mode, IntegerChoice? Count = null,
    int? Lower = null, int? Upper = null);

/// <summary>A requested choice at its containing scope. A null default means no default; input null never means omission.</summary>
public sealed record ControlDefinition(
    [property: JsonRequired] string? Id,
    [property: JsonRequired] string Label,
    [property: JsonRequired] string Type,
    [property: JsonRequired] string Meaning,
    bool Required = false,
    JsonElement? Default = null,
    string? Unit = null,
    int? Min = null,
    int? Max = null,
    int? MaxLength = null,
    ControlOption[]? Options = null);

/// <summary>The visible value is also the submitted choice; meaning explains its educational intent.</summary>
public sealed record ControlOption([property: JsonRequired] string Value, string? Meaning = null);
