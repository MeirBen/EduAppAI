using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Generators;

/// <summary>Dispatches validated blueprints to known generation modes without executing template data.</summary>
public static class TaskGenerator
{
    /// <summary>Generates arithmetic or returns static content to serialize into a new instance snapshot.</summary>
    /// <remarks>Inputs must pass template and parameter validation. Never mutate returned content or nested arrays.</remarks>
    /// <exception cref="ArgumentException">The blueprint selects an unsupported generation mode.</exception>
    public static TaskContent Generate(TaskTemplateDefinition definition,
        IReadOnlyDictionary<string, JsonElement> parameters, int seed) => definition.Generation.Mode switch
        {
            "deterministic" => MathTaskGenerator.Generate(definition, parameters, seed),
            "static" when definition.Generation.Content is { } content => content,
            _ => throw new ArgumentException("Unsupported generation mode.", nameof(definition))
        };
}
