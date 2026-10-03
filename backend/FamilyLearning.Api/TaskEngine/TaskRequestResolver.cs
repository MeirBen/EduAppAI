using System.Security.Cryptography;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using static FamilyLearning.Api.TaskEngine.Validation.EngineValidation;

namespace FamilyLearning.Api.TaskEngine;

/// <summary>Resolves permitted overrides and omitted defaults once, without mutating plan or input.</summary>
public static class TaskRequestResolver
{
    /// <summary>Hashes effective requirements only; an engine revision change alone never makes content stale.</summary>
    public static string Fingerprint(ResolvedTaskRequest request) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.SchemaVersion,
            request.Goal,
            request.Guidance,
            request.Settings,
            request.Materials,
            request.Questions,
            request.Controls,
            request.TotalLength
        }, EngineJson.Options))).ToLowerInvariant();

    /// <summary>Rejects impossible requirements before any count-sized allocation or provider work.</summary>
    public static TaskResolution Resolve(LearningPlan plan, TaskRequest input)
    {
        var errors = LearningPlanValidator.Validate(plan);
        if (errors.Count != 0) return new(null, errors);
        foreach (var error in TaskSettingsValidator.Validate(input.Settings)) errors.AddError(error.Key, error.Value[0]);
        if (errors.Count != 0) return new(null, errors);
        var questionPlan = plan.Questions;
        var formats = questionPlan.Formats.ToArray();
        if (questionPlan.SelectableFormat)
        {
            var selected = input.QuestionFormat.ValueKind == JsonValueKind.Undefined ? questionPlan.DefaultFormat :
                input.QuestionFormat.ValueKind == JsonValueKind.String ? input.QuestionFormat.GetString() : null;
            if (selected is null || !formats.Contains(selected, StringComparer.Ordinal)) errors.AddError("questionFormat", "יש לבחור סוג שאלה מותר.");
            else formats = [selected];
        }
        else if (input.QuestionFormat.ValueKind != JsonValueKind.Undefined) errors.AddError("questionFormat", "סוגי השאלות קבועים בתכנית.");
        if (input.Settings.QuestionCount < formats.Length || input.Settings.QuestionCount < questionPlan.CountBounds?.Min ||
            input.Settings.QuestionCount > questionPlan.CountBounds?.Max)
            errors.AddError("settings.questionCount", "מספר השאלות אינו מתאים לדרישות התכנית.");
        var choices = formats.Contains("single-choice")
            ? ResolveChoice(questionPlan.ChoiceCount, input.ChoiceCount, "choiceCount", errors, MinChoiceCount, MaxChoiceCount) : null;
        if (!formats.Contains("single-choice") && input.ChoiceCount.ValueKind != JsonValueKind.Undefined)
            errors.AddError("choiceCount", "מספר אפשרויות אינו מתאים לסוג השאלה שנבחר.");
        var materialInputs = ReadMap(input.MaterialInputs, "materialInputs", errors);
        var controlValues = ReadMap(input.ControlValues, "controlValues", errors);
        var remainingControls = new HashSet<string>(controlValues.Keys, StringComparer.Ordinal);
        var materials = new ResolvedMaterial[plan.Materials.Length];
        for (var i = 0; i < materials.Length; i++)
        {
            var material = plan.Materials[i];
            materialInputs.Remove(material.Id!, out var submitted);
            var path = $"materialInputs.{material.Id}";
            var values = ReadMap(submitted, path, errors);
            values.Remove("wordCount", out var wordCount);
            values.Remove("sourceText", out var sourceText);
            if (values.Count > 0) errors.AddError(path, "הגדרת חומר אינה מוכרת.");
            var text = material.Text;
            if (material.Source == "per-task")
            {
                if (sourceText.ValueKind != JsonValueKind.String || !HasText(sourceText.GetString(), BodyLimit))
                    errors.AddError(path + ".sourceText", "יש להזין מקור באורך של 1 עד 4,000 תווים.");
                else text = sourceText.GetString();
            }
            else if (sourceText.ValueKind != JsonValueKind.Undefined) errors.AddError(path + ".sourceText", "לא ניתן לשנות את המקור בשדה זה.");
            var length = ResolveLength(material.Length, wordCount, path + ".wordCount", errors);
            materials[i] = new(material.Id!, material.Label, material.Source, material.Guidance, text, length,
                ResolveControls(material.Controls, controlValues, remainingControls, errors));
        }
        if (materialInputs.Count > 0) errors.AddError("materialInputs", "מזהה חומר אינו מוכר.");
        var total = ResolveLength(plan.TotalLength, input.TotalWordCount, "totalWordCount", errors);
        var controls = ResolveControls(plan.Controls, controlValues, remainingControls, errors);
        var questions = new ResolvedQuestions(formats, choices, questionPlan.Guidance,
            ResolveControls(questionPlan.Controls, controlValues, remainingControls, errors));
        if (remainingControls.Count > 0) errors.AddError("controlValues", "מזהה שדה אינו מוכר.");
        if (errors.Count == 0)
            LearningPlanValidator.ValidateFeasibility(input.Settings.QuestionCount, formats, null, choices,
                materials.Select(m => (m.Source, m.Text, m.Length)).ToArray(), total, errors);
        return errors.Count == 0
            ? new(new(plan.SchemaVersion, EngineVersions.Revision, plan.Goal, plan.Guidance,
                input.Settings, materials, questions, controls, total), errors)
            : new(null, errors);
    }

    private static Dictionary<string, JsonElement> ReadMap(JsonElement input, string path, Dictionary<string, string[]> errors)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (input.ValueKind == JsonValueKind.Undefined) return values;
        if (input.ValueKind != JsonValueKind.Object)
        {
            errors.AddError(path, "יש לציין אוסף ערכים תקין, ללא null.");
            return values;
        }
        foreach (var property in input.EnumerateObject())
        {
            if (!values.TryAdd(property.Name, property.Value)) errors.AddError(path, "אין לחזור על אותו שדה.");
            // No legitimate map, including material inputs, needs more entries than the plan-wide control limit.
            if (values.Count > MaxControls)
            {
                errors.AddError(path, "יש יותר מדי שדות.");
                break;
            }
        }
        return values;
    }

    private static ResolvedControl[] ResolveControls(ControlDefinition[] definitions, Dictionary<string, JsonElement> values,
        HashSet<string> remaining, Dictionary<string, string[]> errors)
    {
        var result = new List<ResolvedControl>(definitions.Length);
        foreach (var definition in definitions)
        {
            remaining.Remove(definition.Id!);
            JsonElement? value = values.TryGetValue(definition.Id!, out var supplied) ? supplied :
                definition.Default is { ValueKind: not JsonValueKind.Null } fallback ? fallback : null;
            if (value is null)
            {
                if (definition.Required) errors.AddError($"controlValues.{definition.Id}", "יש למלא את השדה הזה.");
                continue;
            }
            if (LearningPlanValidator.ValidateControlValue(definition, value.Value) is { } error)
                errors.AddError($"controlValues.{definition.Id}", error);
            else result.Add(new(definition.Id!, definition.Label, definition.Type, definition.Meaning, definition.Unit,
                value.Value.Clone(), definition.Type == "select" ? definition.Options!.Single(o => o.Value == value.Value.GetString()).Meaning : null));
        }
        return result.ToArray();
    }

    private static ResolvedLength? ResolveLength(LengthExpectation? length, JsonElement input, string path, Dictionary<string, string[]> errors)
    {
        if (length is null || length.Mode == "range")
        {
            if (input.ValueKind != JsonValueKind.Undefined) errors.AddError(path, "לא ניתן לשנות אורך זה.");
            return LearningPlanValidator.ResolveLength(length);
        }
        return new(length.Mode, ResolveChoice(length.Count, input, path, errors));
    }

    private static int? ResolveChoice(IntegerChoice? choice, JsonElement input, string path, Dictionary<string, string[]> errors,
        int minimum = 1, int maximum = int.MaxValue)
    {
        if (input.ValueKind == JsonValueKind.Undefined) return choice?.Value;
        if (choice is not { Adjustable: true } || input.ValueKind != JsonValueKind.Number || !input.TryGetInt32(out var value) ||
            value < minimum || value > maximum || value < choice.Min || value > choice.Max)
        {
            errors.AddError(path, "יש לבחור מספר שלם בגבולות המותרים ורק כאשר השדה ניתן לשינוי.");
            return null;
        }
        return value;
    }
}
