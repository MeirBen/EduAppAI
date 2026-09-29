using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>Reusable AI instructions and parameters published as one immutable revision.</summary>
/// <remarks>Validate before storage. Treat nested arrays as immutable once published.</remarks>
/// <param name="SchemaVersion">The JSON contract version, independent of the template revision number.</param>
/// <param name="Name">The parent-facing name of this reusable learning idea.</param>
/// <param name="InstanceParameters">Fields a parent can supply for each new instance.</param>
/// <param name="Generation">Instructions and count binding fixed by this template version.</param>
public sealed record TaskTemplateDefinition(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string Name,
    [property: JsonRequired] ParameterDefinition[] InstanceParameters,
    [property: JsonRequired] GenerationDefinition Generation);

/// <summary>Metadata shared by the dynamic form and server-side parameter validation.</summary>
/// <param name="Key">Case-sensitive identifier used in submitted and stored parameter dictionaries.</param>
/// <param name="Label">Human-readable field label.</param>
/// <param name="Type">One of text, integer, select or boolean.</param>
/// <param name="Required">Rejects omission without a default and blank text. Defaults apply regardless of this flag.</param>
/// <param name="Default">Used only for omitted keys; null means no default. Explicit input nulls are invalid.</param>
/// <param name="Min">Inclusive lower bound for integers; null leaves it unbounded.</param>
/// <param name="Max">Inclusive upper bound for integers; null leaves it unbounded.</param>
/// <param name="MaxLength">Text length limit; null uses the validator's default limit.</param>
/// <param name="Options">Allowed case-sensitive values for a select field.</param>
public sealed record ParameterDefinition(
    [property: JsonRequired] string Key,
    [property: JsonRequired] string Label,
    [property: JsonRequired] string Type,
    bool Required = false,
    JsonElement? Default = null,
    int? Min = null,
    int? Max = null,
    int? MaxLength = null,
    string[]? Options = null);

/// <summary>Reusable AI instructions with optional question-count binding and fixed text-length limits.</summary>
public sealed record GenerationDefinition(
    [property: JsonRequired] string Instructions,
    string? QuestionCountParameter = null,
    WordCountRange? ContentWordCount = null);

/// <summary>Inclusive word limits across content blocks; null bounds are open. Omit the range when no length is required.</summary>
/// <remarks>Counts whitespace-separated words, excluding an exact leading task title. At least one bound is required.</remarks>
public sealed record WordCountRange(int? Min = null, int? Max = null);
