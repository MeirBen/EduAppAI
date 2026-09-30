using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;
using static FamilyLearning.Api.TaskEngine.Validation.EngineValidation;

namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Validates canonical identity, supported requirements and resource feasibility without provider access.</summary>
public static class LearningPlanValidator
{
    /// <summary>Returns bounded, application-authored errors; an empty result permits resolution.</summary>
    public static Dictionary<string, string[]> Validate(LearningPlan? plan)
    {
        var errors = new Dictionary<string, string[]>();
        if (plan is null)
        {
            errors.AddError("plan", "יש לציין תכנית למידה.");
            return errors;
        }
        if (plan.SchemaVersion != EngineVersions.SchemaVersion) errors.AddError("schemaVersion", "גרסת התכנית אינה נתמכת.");
        if (!HasText(plan.Name, 100)) errors.AddError("name", "יש להזין שם באורך של 1 עד 100 תווים.");
        if (!HasText(plan.Goal, 500)) errors.AddError("goal", "יש להזין מטרה באורך של 1 עד 500 תווים.");
        if (plan.Guidance is null || plan.Guidance.Length > 4000) errors.AddError("guidance", "ההנחיות מוגבלות ל־4,000 תווים.");
        foreach (var error in TaskSettingsValidator.Validate(plan.Defaults, "defaults")) errors.AddError(error.Key, error.Value[0]);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var controlCount = 0;
        ValidateControls(plan.Controls, "controls", ids, ref controlCount, errors);
        ValidateMaterials(plan.Materials, ids, ref controlCount, errors);
        ValidateQuestions(plan.Questions, plan.Defaults?.QuestionCount, ids, ref controlCount, errors);
        ValidateLength(plan.TotalLength, "totalLength", errors);
        if (plan.TotalLength is not null && plan.Materials is { } materials &&
            (!materials.Any(m => m?.Source == "generated") || materials.Any(m => m?.Length is not null)))
            errors.AddError("totalLength", "יש לבחור אורך כולל או אורך לכל חומר שנוצר, ללא חפיפה.");
        if (controlCount > 16) errors.AddError("controls", "אפשר להגדיר עד 16 שדות בכל התכנית.");
        if (errors.Count == 0)
        {
            if (JsonSerializer.Serialize(plan, EngineJson.Options).Length > PlanLimit) errors.AddError("plan", "התכנית גדולה מדי.");
            ValidateFeasibility(plan.Defaults!.QuestionCount, plan.Questions.Formats,
                plan.Questions.SelectableFormat ? plan.Questions.DefaultFormat : null, plan.Questions.ChoiceCount?.Value,
                plan.Materials.Select(m => (m.Source, m.Text, ResolveLength(m.Length))).ToArray(),
                ResolveLength(plan.TotalLength), errors);
        }
        return errors;
    }

    private static void ValidateMaterials(MaterialDefinition[]? materials, HashSet<string> ids,
        ref int controlCount, Dictionary<string, string[]> errors)
    {
        if (materials is not { Length: <= 4 })
        {
            errors.AddError("materials", "יש לציין עד ארבעה חומרים.");
            return;
        }
        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            var path = $"materials[{i}]";
            if (material is null)
            {
                errors.AddError(path, "יש לציין חומר תקין.");
                continue;
            }
            ValidateId(material.Id, path, ids, errors);
            if (!HasText(material.Label, 100)) errors.AddError(path + ".label", "תווית החומר אינה תקינה.");
            if (material.Guidance is null || material.Guidance.Length > 1000) errors.AddError(path + ".guidance", "ההנחיות מוגבלות ל־1,000 תווים.");
            if (material.Source is not ("generated" or "fixed" or "per-task")) errors.AddError(path + ".source", "סוג המקור אינו נתמך.");
            if (material.Source == "fixed" ? !HasText(material.Text, BodyLimit) : material.Text is not null)
                errors.AddError(path + ".text", "טקסט מקור נדרש רק לחומר קבוע, עד 4,000 תווים.");
            if (material.Source != "generated" && material.Length is not null) errors.AddError(path + ".length", "אורך מבוקש מתאים רק לחומר שנוצר.");
            ValidateLength(material.Length, path + ".length", errors);
            ValidateControls(material.Controls, path + ".controls", ids, ref controlCount, errors);
        }
    }

    private static void ValidateQuestions(QuestionPlan? questions, int? count, HashSet<string> ids,
        ref int controlCount, Dictionary<string, string[]> errors)
    {
        if (questions is null)
        {
            errors.AddError("questions", "יש להגדיר שאלות.");
            return;
        }
        if (questions.Formats is not { Length: >= 1 and <= 3 } || questions.Formats.Any(f => !IsFormat(f)) ||
            questions.Formats.Distinct(StringComparer.Ordinal).Count() != questions.Formats.Length)
            errors.AddError("questions.formats", "יש לבחור סוגי שאלות שונים ונתמכים.");
        else
        {
            if (questions.SelectableFormat ? !questions.Formats.Contains(questions.DefaultFormat, StringComparer.Ordinal) : questions.DefaultFormat is not null)
                errors.AddError("questions.defaultFormat", "ברירת המחדל חייבת להתאים לאפשרות בחירת הסוג.");
            if (!questions.SelectableFormat && count < questions.Formats.Length) errors.AddError("defaults.questionCount", "אין מספיק שאלות לכל הסוגים המבוקשים.");
            if (questions.Formats.Contains("single-choice") != (questions.ChoiceCount is not null)) errors.AddError("questions.choiceCount", "יש להגדיר מספר אפשרויות רק כאשר שאלת בחירה מותרת.");
        }
        if (questions.ChoiceCount is { } choice) ValidateChoice(choice, "questions.choiceCount", errors, 2, 6);
        if (questions.CountBounds is { } bounds && (!ValidBounds(bounds.Min, bounds.Max) || count < bounds.Min || count > bounds.Max))
            errors.AddError("questions.countBounds", "גבולות מספר השאלות או ברירת המחדל אינם תקינים.");
        if (questions.Guidance is null || questions.Guidance.Length > 1000) errors.AddError("questions.guidance", "ההנחיות מוגבלות ל־1,000 תווים.");
        ValidateControls(questions.Controls, "questions.controls", ids, ref controlCount, errors);
    }

    private static bool ValidBounds(int? min, int? max) => min is not < 1 && max is not < 1 && !(min > max);

    private static void ValidateChoice(IntegerChoice choice, string path, Dictionary<string, string[]> errors, int min = 1, int max = int.MaxValue)
    {
        if (choice.Value < min || choice.Value > max || !ValidBounds(choice.Min, choice.Max) ||
            choice.Min < min || choice.Max > max || choice.Value < choice.Min || choice.Value > choice.Max ||
            (!choice.Adjustable && (choice.Min.HasValue || choice.Max.HasValue)))
            errors.AddError(path, "הערך והגבולות חייבים להתאים לאפשרות השינוי ולטווח הנתמך.");
    }

    private static void ValidateLength(LengthExpectation? length, string path, Dictionary<string, string[]> errors)
    {
        if (length is null) return;
        if (length.Mode is "target" or "exact" && length.Count is { } count && length.Lower is null && length.Upper is null)
            ValidateChoice(count, path, errors);
        else if (length.Mode != "range" || length.Count is not null || length.Lower is not > 0 || length.Upper is not > 0 || length.Lower > length.Upper)
            errors.AddError(path, "יש להגדיר יעד, אורך מדויק או טווח חיובי תקין.");
    }

    private static void ValidateControls(ControlDefinition[]? controls, string path, HashSet<string> ids,
        ref int count, Dictionary<string, string[]> errors)
    {
        if (controls is not
            {
                Length: <= 16
            })
        {
            errors.AddError(path, "יש לציין עד 16 שדות.");
            return;
        }
        count += controls.Length;
        for (var i = 0; i < controls.Length; i++)
        {
            var control = controls[i];
            var key = $"{path}[{i}]";
            if (control is null)
            {
                errors.AddError(key, "יש להגדיר שדה תקין.");
                continue;
            }
            ValidateId(control.Id, key, ids, errors);
            if (!HasText(control.Label, 100) || !HasText(control.Meaning, 500) || control.Unit is { Length: > 100 })
                errors.AddError(key, "תווית השדה, המשמעות או היחידה אינן תקינות.");
            if (control.Type is not ("text" or "integer" or "select" or "boolean") ||
                (control.Type != "integer" && (control.Min.HasValue || control.Max.HasValue || control.Unit is not null)) ||
                (control.Type != "text" && control.MaxLength.HasValue) || (control.Type != "select" && control.Options is not null) ||
                control.Min > control.Max || control.MaxLength is < 1 or > 500)
                errors.AddError(key, "סוג השדה וההגבלות אינם תואמים.");
            if (control.Type == "select" && (control.Options is not { Length: >= 1 and <= 20 } ||
                control.Options.Any(o => o is null || !HasText(o.Value, 100) || o.Value != o.Value.Trim() || o.Value.Contains('\n') || o.Value.Contains('\r') || o.Meaning is { Length: > 200 }) ||
                control.Options.Select(o => o.Value).Distinct(StringComparer.Ordinal).Count() != control.Options.Length))
                errors.AddError(key, "יש להגדיר עד 20 אפשרויות שונות ותקינות.");
            if (control.Default is { ValueKind: not JsonValueKind.Null } value && ValidateControlValue(control, value) is { } error)
                errors.AddError(key + ".default", error);
        }
    }

    internal static string? ValidateControlValue(ControlDefinition definition, JsonElement value) => definition.Type switch
    {
        "text" when value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= (definition.MaxLength ?? 500) &&
            (!definition.Required || !string.IsNullOrWhiteSpace(value.GetString())) => null,
        "integer" when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) &&
            !(number < definition.Min) && !(number > definition.Max) => null,
        "boolean" when value.ValueKind is JsonValueKind.True or JsonValueKind.False => null,
        "select" when value.ValueKind == JsonValueKind.String && definition.Options?.Any(o => o?.Value == value.GetString()) == true => null,
        _ => "הערך אינו מתאים לסוג השדה או להגבלותיו."
    };

    internal static ResolvedLength? ResolveLength(LengthExpectation? length) => length is null ? null :
        new(length.Mode, length.Count?.Value, length.Lower, length.Upper);

    internal static void ValidateFeasibility(int count, string[] formats, string? selectedFormat, int? choices,
        (string Source, string? Text, ResolvedLength? Length)[] materials, ResolvedLength? total,
        Dictionary<string, string[]> errors)
    {
        // Widen before multiplication: hostile counts cannot overflow or cause count-sized allocations.
        long minimum = checked(1 + MinimumQuestionLength(count, formats, choices, selectedFormat));
        long generatedMinimum = 0;
        var generatedCount = 0;
        foreach (var material in materials)
        {
            if (material.Source == "generated")
            {
                generatedCount++;
                var bodyMinimum = MinimumLength(material.Length);
                if (bodyMinimum > BodyLimit) errors.AddError("materials", "האורך המבוקש אינו יכול להתאים למגבלת החומר.");
                generatedMinimum = checked(generatedMinimum + bodyMinimum);
            }
            else minimum = checked(minimum + (material.Text?.Length ?? 1));
        }
        // Each body boundary separates words without consuming a whitespace character.
        var totalMinimum = total is { Mode: "exact" or "range" }
            ? Math.Max(generatedCount, checked(2L * (total.Value ?? total.Lower!.Value) - generatedCount))
            : generatedCount;
        if (total is not null && totalMinimum > (long)generatedCount * BodyLimit)
            errors.AddError("totalLength", "האורך הכולל אינו יכול להתאים לחומרים.");
        minimum = checked(minimum + (total is null ? generatedMinimum : Math.Max(generatedMinimum, totalMinimum)));
        if (minimum > ContentLimit) errors.AddError("settings.questionCount", "הדרישות אינן יכולות להתאים למגבלת התוכן.");
    }

    private static long MinimumLength(ResolvedLength? length) => length?.Mode switch
    {
        "exact" => checked(2L * length.Value!.Value - 1),
        "range" => checked(2L * length.Lower!.Value - 1),
        _ => 1
    };
}
