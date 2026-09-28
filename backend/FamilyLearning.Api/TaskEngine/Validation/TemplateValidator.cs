using System.Text.RegularExpressions;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Validation;

public static partial class TemplateValidator
{
    public static Dictionary<string, string[]> Validate(TaskTemplateDefinition definition)
    {
        var errors = new Dictionary<string, string[]>();
        if (definition.SchemaVersion != 1) errors["schemaVersion"] = ["Only schema version 1 is supported."];
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 100)
            errors["name"] = ["Enter a name of 1–100 characters."];
        if (definition.Generation is not
            {
                Mode: "deterministic", Generator: "math-v1",
                FixedSettings.Operation: "multiplication"
            })
            errors["generation"] = ["Choose the supported math-v1 multiplication generator."];
        if (definition.InstanceParameters is not { Length: > 0 and <= 16 } parameters)
        {
            errors["instanceParameters"] = ["Provide between 1 and 16 parameters."];
            return errors;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            var error = ValidateParameter(parameter, keys);
            if (error is not null) errors[$"instanceParameters[{index}]"] = [error];
        }

        var difficulty = parameters.FirstOrDefault(p => p?.Key == "difficulty");
        if (difficulty is not { Type: "select", Required: true, Options.Length: 3 } ||
            !difficulty.Options.ToHashSet(StringComparer.Ordinal).SetEquals(["easy", "medium", "hard"]))
            errors["difficulty"] = ["Math requires a difficulty select with easy, medium and hard options."];
        var count = parameters.FirstOrDefault(p => p?.Key == "questionCount");
        if (count is not { Type: "integer", Required: true, Min: >= 1, Max: <= 20 })
            errors["questionCount"] = ["Math requires an integer questionCount with bounds within 1–20."];
        return errors;
    }

    private static string? ValidateParameter(ParameterDefinition? parameter, HashSet<string> keys)
    {
        if (parameter is null) return "A parameter cannot be null.";
        if (parameter.Key is null || !ParameterKey().IsMatch(parameter.Key) || !keys.Add(parameter.Key))
            return "Use a unique key beginning with a lowercase letter, followed by letters or digits (40 maximum).";
        if (string.IsNullOrWhiteSpace(parameter.Label) || parameter.Label.Length > 100)
            return "Enter a label of 1–100 characters.";
        if (parameter.Type is not ("text" or "integer" or "select" or "boolean"))
            return "Unsupported parameter type.";
        if (parameter.Min > parameter.Max || parameter.MaxLength is < 1 or > 500)
            return "Invalid parameter bounds.";
        if (parameter.Type == "select" &&
            (parameter.Options is not { Length: > 0 and <= 20 } ||
             parameter.Options.Any(option => string.IsNullOrWhiteSpace(option) || option.Length > 100) ||
             parameter.Options.Distinct(StringComparer.Ordinal).Count() != parameter.Options.Length))
            return "Provide 1–20 unique, nonempty options of at most 100 characters.";
        return parameter.Default is { } value ? ParameterValidator.ValidateValue(parameter, value) : null;
    }

    [GeneratedRegex("^[a-z][a-zA-Z0-9]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterKey();
}
