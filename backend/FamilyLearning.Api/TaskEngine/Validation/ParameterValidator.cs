using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Validation;

public sealed record ParameterValidationResult(
    Dictionary<string, JsonElement> Values,
    Dictionary<string, string[]> Errors);

public static class ParameterValidator
{
    public static ParameterValidationResult Validate(
        IReadOnlyList<ParameterDefinition> definitions,
        IReadOnlyDictionary<string, JsonElement>? supplied)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (supplied is null)
        {
            errors["parameters"] = ["Parameters are required. Use an empty object to accept defaults."];
            return new(values, errors);
        }
        var knownKeys = definitions.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in supplied.Keys)
        {
            if (!knownKeys.Contains(key)) errors[key] = ["Unknown parameter."];
        }

        foreach (var definition in definitions)
        {
            if (!supplied.TryGetValue(definition.Key, out var value))
            {
                if (definition.Default is { } defaultValue) value = defaultValue;
                else
                {
                    if (definition.Required) errors[definition.Key] = ["This field is required."];
                    continue;
                }
            }

            var error = ValidateValue(definition, value);
            if (error is not null) errors[definition.Key] = [error];
            else values.Add(definition.Key, value.Clone());
        }

        return new(values, errors);
    }

    internal static string? ValidateValue(ParameterDefinition definition, JsonElement value)
    {
        switch (definition.Type)
        {
            case "text":
                if (value.ValueKind != JsonValueKind.String) return "Enter text.";
                var text = value.GetString()!;
                if (definition.Required && string.IsNullOrWhiteSpace(text)) return "This field is required.";
                return text.Length > (definition.MaxLength ?? 500) ? "The text is too long." : null;
            case "integer":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
                    return "Enter a whole number.";
                return number < definition.Min || number > definition.Max ? "The number is outside the allowed range." : null;
            case "select":
                return value.ValueKind == JsonValueKind.String &&
                    definition.Options?.Contains(value.GetString(), StringComparer.Ordinal) == true
                    ? null : "Choose one of the available options.";
            case "boolean":
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "Choose true or false.";
            default:
                return "Unsupported parameter type.";
        }
    }
}
