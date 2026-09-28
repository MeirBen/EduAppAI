using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

public sealed record TaskTemplateDefinition(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string Name,
    [property: JsonRequired] ParameterDefinition[] InstanceParameters,
    [property: JsonRequired] GenerationDefinition Generation);

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

// Add another generation contract when a second generator is implemented.
public sealed record GenerationDefinition(
    [property: JsonRequired] string Mode,
    [property: JsonRequired] string Generator,
    [property: JsonRequired] MathSettings FixedSettings);
public sealed record MathSettings([property: JsonRequired] string Operation);
