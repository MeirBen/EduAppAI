using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Resolved values and field errors from one validation pass.</summary>
/// <param name="Values">Accepted values, including defaults; use only when Errors is empty.</param>
/// <param name="Errors">Errors keyed by parameter name, suitable for an HTTP validation problem.</param>
public sealed record ParameterValidationResult(
    Dictionary<string, JsonElement> Values,
    Dictionary<string, string[]> Errors);

/// <summary>Resolves defaults and validates instance values against a published parameter schema.</summary>
public static class ParameterValidator
{
    /// <summary>Validates supplied values without changing the schema or input dictionary.</summary>
    /// <param name="definitions">Unique parameter definitions accepted by the template validator.</param>
    /// <param name="supplied">Submitted values; null is invalid, while an empty dictionary requests defaults.</param>
    /// <returns>Cloned accepted values and any errors. A nonempty error dictionary prevents generation.</returns>
    /// <remarks>Unknown keys are rejected. Explicit nulls do not fall back to defaults.</remarks>
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

            // Clone accepted values so the result survives disposal of the caller's JSON document.
            var error = ValidateValue(definition, value);
            if (error is not null) errors[definition.Key] = [error];
            else values.Add(definition.Key, value.Clone());
        }

        return new(values, errors);
    }

    /// <summary>Checks one value; shared with template validation so defaults obey the same rules.</summary>
    /// <returns>A field error, or null when the value is valid.</returns>
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
