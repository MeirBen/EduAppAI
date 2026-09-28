using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>The versioned blueprint stored when a parent publishes a template.</summary>
/// <remarks>Validate before storage. Treat nested arrays as immutable once published.</remarks>
/// <param name="SchemaVersion">The JSON contract version, independent of the template revision number.</param>
/// <param name="Name">The display name copied into newly generated content.</param>
/// <param name="InstanceParameters">Fields a parent can supply for each new instance.</param>
/// <param name="Generation">The supported generator and settings fixed by this template version.</param>
public sealed record TaskTemplateDefinition(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string Name,
    [property: JsonRequired] ParameterDefinition[] InstanceParameters,
    [property: JsonRequired] GenerationDefinition Generation);

/// <summary>Metadata shared by the dynamic form and server-side parameter validation.</summary>
/// <param name="Key">Case-sensitive identifier used in submitted and stored parameter dictionaries.</param>
/// <param name="Label">Human-readable field label.</param>
/// <param name="Type">One of text, integer, select or boolean.</param>
/// <param name="Required">Whether omission is invalid when no default exists.</param>
/// <param name="Default">Value used only when the submitted dictionary omits the key.</param>
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

/// <summary>Selects the implemented deterministic math generator.</summary>
/// <remarks>Add a separate contract when another generation mode is implemented.</remarks>
public sealed record GenerationDefinition(
    [property: JsonRequired] string Mode,
    [property: JsonRequired] string Generator,
    [property: JsonRequired] MathSettings FixedSettings);
/// <summary>Math behavior fixed at publication; currently only multiplication is supported.</summary>
public sealed record MathSettings([property: JsonRequired] string Operation);
