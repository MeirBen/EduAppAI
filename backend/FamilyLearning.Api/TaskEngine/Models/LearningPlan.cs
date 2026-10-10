using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>Application-owned learning requirements. Validate before use; nested collections remain mutable.</summary>
/// <remarks>Proposals share this shape but may have null new IDs. Only normalized, validated plans are canonical.
/// <see cref="DocumentGuidance"/> governs only the learner title and instructions, so it stales no content.</remarks>
public sealed record LearningPlan(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Goal,
    [property: JsonRequired] string Guidance,
    [property: JsonRequired] TaskSettings Settings,
    [property: JsonRequired] MaterialDefinition[] Materials,
    [property: JsonRequired] QuestionPlan Questions,
    LengthExpectation? TotalLength = null,
    [property: JsonRequired] string DocumentGuidance = "",
    [property: JsonRequired] int SchemaVersion = EngineVersions.SchemaVersion);

/// <summary>A generated or verbatim supplied source owned by this activity.</summary>
public sealed record MaterialDefinition(
    [property: JsonRequired] string? Id,
    [property: JsonRequired] string Label,
    [property: JsonRequired] string Source,
    [property: JsonRequired] string Guidance,
    string? Text,
    LengthExpectation? Length);

/// <summary>Every allowed format must appear; choice count applies only to single-choice questions.</summary>
public sealed record QuestionPlan(
    [property: JsonRequired] string[] Formats,
    [property: JsonRequired] int? ChoiceCount,
    [property: JsonRequired] string Guidance);

/// <summary>An approximate target uses Count; a strict inclusive range uses Lower below Upper instead.</summary>
public sealed record LengthExpectation([property: JsonRequired] string Mode, int? Count = null,
    int? Lower = null, int? Upper = null);
