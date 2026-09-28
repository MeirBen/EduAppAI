using System.Text.RegularExpressions;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Checks a reusable AI blueprint and its generic parameter definitions.</summary>
public static partial class TemplateValidator
{
    /// <summary>Validates an untrusted blueprint before publication or generation.</summary>
    /// <param name="definition">The submitted blueprint, including potentially null nested JSON members.</param>
    /// <returns>Hebrew field errors suitable for a validation problem; an empty dictionary means success.</returns>
    public static Dictionary<string, string[]> Validate(TaskTemplateDefinition? definition)
    {
        var errors = new Dictionary<string, string[]>();
        if (definition is null)
        {
            errors["definition"] = ["יש לציין את הגדרות התבנית."];
            return errors;
        }
        if (definition.SchemaVersion != 2) errors["schemaVersion"] = ["גרסת מבנה התבנית אינה נתמכת."];
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 100)
            errors["name"] = ["יש להזין שם באורך של 1 עד 100 תווים."];
        if (string.IsNullOrWhiteSpace(definition.Generation?.Instructions) || definition.Generation.Instructions.Length > 4000)
            errors["generation.instructions"] = ["יש להזין הנחיות ליצירת תוכן באורך של 1 עד 4,000 תווים."];
        if (definition.InstanceParameters is not { Length: <= 16 } parameters)
        {
            errors["instanceParameters"] = ["יש לציין רשימת שדות, עד 16 שדות."];
            return errors;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            var error = ValidateParameter(parameter, keys);
            if (error is not null) errors[$"instanceParameters[{index}]"] = [error];
        }

        var countKey = definition.Generation?.QuestionCountParameter;
        if (countKey is not null && parameters.FirstOrDefault(p => p?.Key == countKey) is not
            { Type: "integer", Required: true, Min: >= 1, Max: <= 20 })
            errors["generation.questionCountParameter"] = ["יש לקשר את מספר השאלות לשדה מספרי נדרש, עם גבולות בין 1 ל־20."];
        return errors;
    }

    private static string? ValidateParameter(ParameterDefinition? parameter, HashSet<string> keys)
    {
        if (parameter is null) return "הגדרת שדה אינה יכולה להיות ריקה.";
        if (parameter.Key is null || !ParameterKey().IsMatch(parameter.Key) || !keys.Add(parameter.Key))
            return "מפתח השדה חייב להיות ייחודי, להתחיל באות לטינית קטנה ולהכיל עד 40 אותיות לטיניות וספרות.";
        if (string.IsNullOrWhiteSpace(parameter.Label) || parameter.Label.Length > 100)
            return "יש להזין תווית באורך של 1 עד 100 תווים.";
        if (parameter.Type is not ("text" or "integer" or "select" or "boolean"))
            return "סוג השדה אינו נתמך.";
        if (parameter.Min > parameter.Max || parameter.MaxLength is < 1 or > 500)
            return "גבולות השדה אינם תקינים.";
        if (parameter.Type == "select" &&
            (parameter.Options is not { Length: > 0 and <= 20 } ||
             parameter.Options.Any(option => string.IsNullOrWhiteSpace(option) || option.Length > 100 || option != option.Trim() || option.Contains('\n') || option.Contains('\r')) ||
             parameter.Options.Distinct(StringComparer.Ordinal).Count() != parameter.Options.Length))
            return "יש להגדיר בין אפשרות אחת ל־20 אפשרויות שונות, שאינן ריקות, באורך של עד 100 תווים.";
        return parameter.Default is { } value ? ParameterValidator.ValidateValue(parameter, value) : null;
    }

    [GeneratedRegex("\\A[a-z][a-zA-Z0-9]{0,39}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterKey();
}
