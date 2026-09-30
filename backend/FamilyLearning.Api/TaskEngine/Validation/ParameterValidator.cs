using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Resolved values and field errors from one validation pass.</summary>
/// <param name="Values">Accepted values, including defaults; use only when Errors is empty.</param>
/// <param name="Errors">Hebrew feedback keyed by JSON input path, suitable for an HTTP validation problem.</param>
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
            errors["parameters"] = ["יש לציין את הגדרות התרגול. אפשר להשתמש בברירות המחדל."];
            return new(values, errors);
        }
        var knownKeys = definitions.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in supplied.Keys)
        {
            if (!knownKeys.Contains(key)) errors[$"parameters.{key}"] = ["ההגדרה אינה מוכרת."];
        }

        foreach (var definition in definitions)
        {
            var value = supplied.TryGetValue(definition.Key, out var input) ? input : definition.Default;
            if (value is not { } resolved)
            {
                if (definition.Required) errors[$"parameters.{definition.Key}"] = ["יש למלא את השדה הזה."];
                continue;
            }

            // Clone accepted values so the result survives disposal of the caller's JSON document.
            var error = ValidateValue(definition, resolved);
            if (error is not null) errors[$"parameters.{definition.Key}"] = [error];
            else values.Add(definition.Key, resolved.Clone());
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
                if (value.ValueKind != JsonValueKind.String) return "יש להזין טקסט.";
                var text = value.GetString()!;
                if (definition.Required && string.IsNullOrWhiteSpace(text)) return "יש למלא את השדה הזה.";
                return text.Length > (definition.MaxLength ?? 500) ? "הטקסט ארוך מדי." : null;
            case "integer":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
                    return "יש להזין מספר שלם.";
                return number < definition.Min || number > definition.Max ? "המספר מחוץ לטווח המותר." : null;
            case "select":
                return value.ValueKind == JsonValueKind.String &&
                    definition.Options?.Contains(value.GetString(), StringComparer.Ordinal) == true
                    ? null : "יש לבחור אחת מהאפשרויות הזמינות.";
            case "boolean":
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "יש לבחור כן או לא.";
            default:
                return "סוג השדה אינו נתמך.";
        }
    }
}
