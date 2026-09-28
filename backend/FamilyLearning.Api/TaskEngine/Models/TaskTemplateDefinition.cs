using System.Text.Json;

namespace FamilyLearning.Api.TaskEngine.Models;

public sealed record TaskTemplateDefinition(
    int SchemaVersion,
    string Name,
    ParameterDefinition[] InstanceParameters,
    GenerationDefinition Generation);

public sealed record ParameterDefinition(
    string Key,
    string Label,
    string Type,
    bool Required = false,
    JsonElement? Default = null,
    int? Min = null,
    int? Max = null,
    int? MaxLength = null,
    string[]? Options = null);

// Add another generation contract when a second generator is implemented.
public sealed record GenerationDefinition(string Mode, string Generator, MathSettings FixedSettings);
public sealed record MathSettings(string Operation);
