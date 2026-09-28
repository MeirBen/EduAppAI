using System.Globalization;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Generators;

/// <summary>Generates multiplication content without HTTP, storage or shared random state.</summary>
public static class MathTaskGenerator
{
    /// <summary>Creates questions and their answer keys from validated inputs.</summary>
    /// <param name="definition">A blueprint accepted by the template validator.</param>
    /// <param name="parameters">Resolved values from successful parameter validation, including defaults.</param>
    /// <param name="seed">Seed for a random sequence local to this call.</param>
    /// <returns>New content to freeze in an instance before returning it to the parent.</returns>
    /// <remarks>
    /// Identical inputs reproduce content for the same generator and runtime implementation.
    /// Persist the content itself: a seed is diagnostic data, not a cross-version replay contract.
    /// </remarks>
    /// <exception cref="ArgumentException">The difficulty is unsupported.</exception>
    public static TaskContent Generate(
        TaskTemplateDefinition definition, IReadOnlyDictionary<string, JsonElement> parameters, int seed)
    {
        var maximumOperand = parameters["difficulty"].GetString() switch
        {
            "easy" => 5,
            "medium" => 10,
            "hard" => 12,
            _ => throw new ArgumentException("Unsupported difficulty.", nameof(parameters))
        };
        var questions = new TaskQuestion[parameters["questionCount"].GetInt32()];
        var random = new Random(seed);
        for (var index = 0; index < questions.Length; index++)
        {
            var left = random.Next(1, maximumOperand + 1);
            var right = random.Next(1, maximumOperand + 1);
            questions[index] = new($"q{index + 1}", $"{left} × {right}", new("numeric-input"),
                new((left * right).ToString(CultureInfo.InvariantCulture)), 1);
        }

        return new(definition.Name, "Multiply the two numbers.", [], questions);
    }
}
