using System.Globalization;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Generators;

/// <summary>Generates arithmetic content without HTTP, storage or shared random state.</summary>
public static class MathTaskGenerator
{
    /// <summary>Creates questions and their answer keys from validated inputs.</summary>
    /// <param name="definition">A blueprint accepted by the template validator.</param>
    /// <param name="parameters">Resolved values from successful parameter validation, including defaults.</param>
    /// <param name="seed">Seed for a random sequence local to this call.</param>
    /// <returns>New content with Hebrew instructions to freeze before returning it to the parent.</returns>
    /// <remarks>
    /// Identical inputs reproduce content for the same generator and runtime implementation.
    /// Persist the content itself: a seed is diagnostic data, not a cross-version replay contract.
    /// </remarks>
    /// <exception cref="ArgumentException">The difficulty or operation is unsupported.</exception>
    public static TaskContent Generate(
        TaskTemplateDefinition definition, IReadOnlyDictionary<string, JsonElement> parameters, int seed)
    {
        var operation = definition.Generation.FixedSettings?.Operation;
        var largerOperands = operation is "addition" or "subtraction";
        var maximumOperand = parameters["difficulty"].GetString() switch
        {
            "easy" => largerOperands ? 10 : 5,
            "medium" => largerOperands ? 50 : 10,
            "hard" => largerOperands ? 100 : 12,
            _ => throw new ArgumentException("Unsupported difficulty.", nameof(parameters))
        };
        var questions = new TaskQuestion[parameters["questionCount"].GetInt32()];
        var random = new Random(seed);
        for (var index = 0; index < questions.Length; index++)
        {
            var left = random.Next(1, maximumOperand + 1);
            var right = random.Next(1, maximumOperand + 1);
            var (prompt, answer) = operation switch
            {
                "addition" => ($"{left} + {right}", left + right),
                "subtraction" => ($"{Math.Max(left, right)} − {Math.Min(left, right)}", Math.Abs(left - right)),
                "multiplication" => ($"{left} × {right}", left * right),
                // Generate from factors so every quotient is a whole number and the divisor is nonzero.
                "division" => ($"{left * right} ÷ {right}", left),
                _ => throw new ArgumentException("Unsupported operation.", nameof(definition))
            };
            questions[index] = new($"q{index + 1}", prompt, new("numeric-input"),
                new(answer.ToString(CultureInfo.InvariantCulture)), 1);
        }

        var instructions = operation switch
        {
            "addition" => "מהו הסכום של שני המספרים?",
            "subtraction" => "מהו ההפרש בין שני המספרים?",
            "division" => "מהי תוצאת החילוק?",
            _ => "מהי המכפלה של שני המספרים?"
        };
        return new(definition.Name, instructions, [], questions);
    }
}
