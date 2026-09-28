using System.Globalization;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Generators;

public static class MathTaskGenerator
{
    // Call only after template and parameter validation. The seed makes failures reproducible.
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
