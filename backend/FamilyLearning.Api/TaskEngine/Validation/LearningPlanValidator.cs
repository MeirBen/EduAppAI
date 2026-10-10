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
        if (!HasText(plan.Name, NameLength)) errors.AddError("name", $"יש להזין שם באורך של 1 עד {NameLength} תווים.");
        if (!HasText(plan.Goal, GoalLength)) errors.AddError("goal", $"יש להזין מטרה באורך של 1 עד {GoalLength} תווים.");
        if (plan.Guidance is null || plan.Guidance.Length > GuidanceLength) errors.AddError("guidance", $"ההנחיות מוגבלות ל־{Count(GuidanceLength)} תווים.");
        if (plan.DocumentGuidance is null || plan.DocumentGuidance.Length > ScopedGuidanceLength)
            errors.AddError("documentGuidance", $"ההנחיות לכותרת ולהוראות מוגבלות ל־{Count(ScopedGuidanceLength)} תווים.");
        foreach (var error in TaskSettingsValidator.Validate(plan.Settings, "settings")) errors.AddError(error.Key, error.Value[0]);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        ValidateMaterials(plan.Materials, ids, errors);
        ValidateQuestions(plan.Questions, plan.Settings?.QuestionCount, errors);
        ValidateLength(plan.TotalLength, "totalLength", errors);
        if (plan.TotalLength is not null && plan.Materials is { } materials &&
            (!materials.Any(m => m?.Source == "generated") || materials.Any(m => m?.Length is not null)))
            errors.AddError("totalLength", "יש לבחור אורך כולל או אורך לכל טקסט שנוצר, לא את שניהם.");
        if (errors.Count == 0)
        {
            if (JsonSerializer.Serialize(plan, EngineJson.Options).Length > PlanLimit) errors.AddError("plan", "ההגדרות גדולות מדי.");
            ValidateFeasibility(plan.Settings!.QuestionCount, plan.Questions.Formats,
                plan.Questions.ChoiceCount,
                plan.Materials.Select(m => (m.Source, m.Text, ResolveLength(m.Length))).ToArray(),
                ResolveLength(plan.TotalLength), errors);
        }
        return errors;
    }

    private static void ValidateMaterials(MaterialDefinition[]? materials, HashSet<string> ids,
        Dictionary<string, string[]> errors)
    {
        if (materials is not { Length: <= MaxMaterials })
        {
            errors.AddError("materials", $"יש לציין עד {MaxMaterials} חומרים.");
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
            if (!HasText(material.Label, NameLength)) errors.AddError(path + ".label", "תווית החומר אינה תקינה.");
            if (material.Guidance is null || material.Guidance.Length > ScopedGuidanceLength)
                errors.AddError(path + ".guidance", $"ההנחיות מוגבלות ל־{Count(ScopedGuidanceLength)} תווים.");
            if (material.Source is not ("generated" or "supplied")) errors.AddError(path + ".source", "סוג המקור אינו נתמך.");
            if (material.Source == "supplied" ? !HasText(material.Text, BodyLimit) : material.Text is not null)
                errors.AddError(path + ".text", $"טקסט משלכם נדרש רק כשבוחרים מקור קבוע, עד {Count(BodyLimit)} תווים.");
            if (material.Source != "generated" && material.Length is not null) errors.AddError(path + ".length", "אורך מבוקש מתאים רק לחומר שנוצר.");
            ValidateLength(material.Length, path + ".length", errors);
        }
    }

    private static void ValidateQuestions(QuestionPlan? questions, int? count,
        Dictionary<string, string[]> errors)
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
            if (count < questions.Formats.Length) errors.AddError("settings.questionCount", "אין מספיק שאלות לכל הסוגים המבוקשים.");
            if (questions.Formats.Contains("single-choice") != (questions.ChoiceCount is not null)) errors.AddError("questions.choiceCount", "יש להגדיר מספר אפשרויות רק כאשר שאלת בחירה מותרת.");
        }
        if (questions.ChoiceCount is { } choice) ValidateChoice(choice, "questions.choiceCount", errors, MinChoiceCount, MaxChoiceCount);
        if (questions.Guidance is null || questions.Guidance.Length > ScopedGuidanceLength)
            errors.AddError("questions.guidance", $"ההנחיות מוגבלות ל־{Count(ScopedGuidanceLength)} תווים.");
    }

    private static void ValidateChoice(int choice, string path, Dictionary<string, string[]> errors, int min = 1, int max = int.MaxValue)
    {
        if (choice < min || choice > max) errors.AddError(path, "הערך מחוץ לטווח הנתמך.");
    }

    // A strict range needs room between its ends; an equal pair would reintroduce exact counts that generation cannot meet reliably.
    private static void ValidateLength(LengthExpectation? length, string path, Dictionary<string, string[]> errors)
    {
        if (length is null) return;
        if (length.Mode == "target" && length.Count is { } count && length.Lower is null && length.Upper is null)
            ValidateChoice(count, path, errors);
        else if (length.Mode != "range" || length.Count is not null || length.Lower is not > 0 || length.Upper is not > 0 || length.Lower >= length.Upper)
            errors.AddError(path, "יש להגדיר אורך משוער, או טווח שבו המינימום קטן מהמקסימום.");
    }

    internal static ResolvedLength? ResolveLength(LengthExpectation? length) => length is null ? null :
        new(length.Mode, length.Count, length.Lower, length.Upper);

    private static void ValidateFeasibility(int count, string[] formats, int? choices,
        (string Source, string? Text, ResolvedLength? Length)[] materials, ResolvedLength? total,
        Dictionary<string, string[]> errors)
    {
        // Widen before multiplication: hostile counts cannot overflow or cause count-sized allocations.
        long minimum = checked(1 + MinimumQuestionLength(count, formats, choices));
        long generatedMinimum = 0;
        var generatedCount = 0;
        foreach (var material in materials)
        {
            if (material.Source == "generated")
            {
                generatedCount++;
                var bodyMinimum = MinimumLength(material.Length);
                if (bodyMinimum > BodyLimit) errors.AddError("materials", "האורך המבוקש חורג מהאורך המרבי של טקסט.");
                generatedMinimum = checked(generatedMinimum + bodyMinimum);
            }
            else minimum = checked(minimum + (material.Text?.Length ?? 1));
        }
        // Each body boundary separates words without consuming a whitespace character.
        var totalMinimum = total is { Mode: "range" }
            ? Math.Max(generatedCount, checked(2L * total.Lower!.Value - generatedCount))
            : generatedCount;
        if (total is not null && totalMinimum > (long)generatedCount * BodyLimit)
            errors.AddError("totalLength", "האורך הכולל אינו מתאים לטקסטים שהוגדרו.");
        minimum = checked(minimum + (total is null ? generatedMinimum : Math.Max(generatedMinimum, totalMinimum)));
        if (minimum > ContentLimit) errors.AddError("settings.questionCount", "הדרישות אינן יכולות להתאים למגבלת התוכן.");
    }

    private static long MinimumLength(ResolvedLength? length) =>
        length is { Mode: "range" } ? checked(2L * length.Lower!.Value - 1) : 1;
}
