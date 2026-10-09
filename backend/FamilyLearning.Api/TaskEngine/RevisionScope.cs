using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.TaskEngine;

/// <summary>A bounded deterministic scope. A clarification never authorizes working content to be applied.</summary>
public sealed record RevisionWork(string[] NewMaterials, MaterialRewrite[] Rewrites, string Questions,
    ContentEdit[] QuestionEdits, string? Instruction, string[]? QuestionOrder, bool RequiresComplete, string? Clarification = null);

/// <summary>One existing generated text, in plan order, with an optional self-contained edit.</summary>
public sealed record MaterialRewrite(string Id, string? Instruction);

/// <summary>Derives dependencies by effective value; the planner cannot narrow required generation.</summary>
public static class RevisionScope
{
    private const string DependencyChoice = "חלק מהתוכן חסר או אינו עדכני. יש ליצור, לתקן או לאשר אותו לפני השינוי המבוקש.";
    private const string LengthChoice = "האורך הכולל המדויק דורש חלוקה בין כמה טקסטים. אפשר לבחור טווח לכל טקסט או אורך כולל משוער.";

    public static RevisionWork Derive(LearningPlan before, TaskDocument document, RevisionChange change)
    {
        var after = change.Plan;
        var none = new RevisionWork([], [], "none", [], null, null, false);
        if (!ActivityRevisionValidator.HasGeneratedContent(before, document)) return none;
        var shared = before.Goal != after.Goal || before.Guidance != after.Guidance ||
            !Equal(before.Settings! with { QuestionCount = 1 }, after.Settings! with { QuestionCount = 1 }) || !Equal(before.TotalLength, after.TotalLength);
        var newMaterials = after.Materials.Where(m => m.Source == "generated" && !before.Materials.Any(old => old.Id == m.Id)).Select(m => m.Id!).ToArray();
        var rewrites = after.Materials.Where(m => m.Source == "generated" && before.Materials.Any(old => old.Id == m.Id) &&
                (shared || !MaterialRequirementsEqual(before.Materials.First(old => old.Id == m.Id), m) || change.MaterialEdits.Any(e => e.Id == m.Id)))
            .Select(m => new MaterialRewrite(m.Id!, change.MaterialEdits.FirstOrDefault(e => e.Id == m.Id)?.Instruction)).ToArray();
        var materialChange = !MaterialRequirementsEqual(before.Materials, after.Materials) || rewrites.Length > 0;
        var questionChange = !Equal(before.Questions with { Formats = before.Questions.Formats.Order(StringComparer.Ordinal).ToArray() },
            after.Questions with { Formats = after.Questions.Formats.Order(StringComparer.Ordinal).ToArray() });
        var countChange = after.Settings!.QuestionCount - before.Settings!.QuestionCount;
        var full = shared || materialChange || questionChange || change.Questions.Scope == "all" ||
            countChange != 0 && change.Questions.Scope == "selected";
        var questions = full ? "all" : change.QuestionOrder is not null ? "preserve" : countChange > 0 ? "append" :
            change.Questions.Scope == "selected" ? "selected" : "none";
        var work = new RevisionWork(newMaterials, rewrites, questions, change.Questions.Items,
            RebuildInstruction(change, full, document), change.QuestionOrder, full || questions != "none");
        if (!work.RequiresComplete) return countChange < 0 ? work with { Clarification = "יש לבחור אילו שאלות להסיר או באיזה סדר להשאיר אותן." } : work;

        if (after.TotalLength is { Mode: "range" } && (rewrites.Length > 1 ||
            newMaterials.Length > 0 && after.Materials.Any(m => m.Source == "generated" && !newMaterials.Contains(m.Id))))
            return work with { Clarification = LengthChoice };
        var prior = TaskRequestResolver.ResolveOrThrow(before);
        var oldMaterials = TaskDocumentValidator.ValidateMaterials(prior, document);
        foreach (var material in after.Materials.Where(m => m.Source == "generated" && !newMaterials.Contains(m.Id) && !rewrites.Any(r => r.Id == m.Id)))
            if (!document.Materials.Any(m => m.Id == material.Id) || oldMaterials.Errors.Count > 0 ||
                oldMaterials.Diagnostics.Keys.Any(key => key.StartsWith($"materials.{material.Id}", StringComparison.Ordinal) || key == $"length.{material.Id}"))
                return work with { Clarification = DependencyChoice };
        // Preservation may change the fingerprint, never the content or its previous validity.
        if (questions is "preserve" or "append" or "selected" && TaskDocumentValidator.ValidateRelease(prior, document).Count > 0)
            return work with { Clarification = DependencyChoice };
        var prepared = PrepareDocument(before, document, after, work);
        var next = TaskRequestResolver.ResolveOrThrow(after);
        if (after.TotalLength is { Mode: "range" } && newMaterials.Length == 0 && rewrites.Length == 0 &&
            TextLength.Measure(next, prepared).Any(m => m.Scope == "total" && m.Satisfied == false))
            return work with { Clarification = LengthChoice };
        if (questions == "preserve" && TaskDocumentValidator.ValidateRelease(next, prepared).Count > 0)
            return work with { Clarification = "סדר השאלות המבוקש חייב להשאיר לפחות שאלה אחת ולכלול את כל סוגי השאלות שנדרשו." };
        return work;
    }

    public static RevisionWork ForCreate(LearningPlan plan, TaskDocument current)
    {
        var input = TaskRequestResolver.ResolveOrThrow(plan);
        var check = TaskDocumentValidator.ValidateMaterials(input, current);
        var missing = plan.Materials.Where(m => m.Source == "generated" && !current.Materials.Any(c => c.Id == m.Id)).Select(m => m.Id!).ToArray();
        if (check.Errors.Count > 0 || check.Diagnostics.Keys.Any(key => !missing.Any(id => key == $"materials.{id}" || key == $"length.{id}") && key != "length.total"))
            throw new TaskValidationException("materials", DependencyChoice);
        if (plan.TotalLength is { Mode: "range" } && missing.Length > 0 && current.Materials.Any(m => plan.Materials.Any(r => r.Id == m.Id && r.Source == "generated")))
            throw new TaskValidationException("totalLength", LengthChoice);
        if (missing.Length == 0 && check.Diagnostics.Count > 0) throw new TaskValidationException(check.Diagnostics);
        if (missing.Length == 0 && TaskDocumentValidator.ValidateRelease(input, current).Count == 0)
            throw new TaskValidationException("document", "התוכן כבר שלם. אפשר לבקש שינוי בשיחה או ליצור שאלות מחדש במפורש.");
        return new(missing, [], "all", [], null, null, true);
    }

    /// <summary>Aligns sources/order and rebases only previously current unaffected content. No approval or adoption is granted.</summary>
    public static TaskDocument PrepareDocument(LearningPlan before, TaskDocument document, LearningPlan after, RevisionWork work)
    {
        var oldInput = TaskRequestResolver.ResolveOrThrow(before);
        var input = TaskRequestResolver.ResolveOrThrow(after);
        var previousFingerprint = TaskRequestResolver.Fingerprint(oldInput);
        var fingerprint = TaskRequestResolver.Fingerprint(input);
        var aligned = TaskAssembly.AlignSources(input, document);
        var oldCheck = TaskDocumentValidator.ValidateMaterials(oldInput, document);
        var materials = aligned.Materials.Select(m => !work.Rewrites.Any(r => r.Id == m.Id) && m.Acceptance is { } acceptance &&
            acceptance.InputFingerprint == previousFingerprint && oldCheck.Errors.Count == 0 &&
            !oldCheck.Diagnostics.Keys.Any(k => k.StartsWith($"materials.{m.Id}", StringComparison.Ordinal) || k == $"length.{m.Id}")
            ? m with { Acceptance = acceptance with { InputFingerprint = fingerprint } } : m).ToArray();
        var questions = work.Questions == "preserve" ? work.QuestionOrder!.Select(id => document.Questions.First(q => q.Id == id)).ToArray() : document.Questions;
        if (work.Questions is "preserve" or "append" or "selected" && TaskDocumentValidator.ValidateRelease(oldInput, document).Count == 0)
            questions = questions.Select(q => q with { Acceptance = q.Acceptance! with { InputFingerprint = fingerprint } }).ToArray();
        else if (work.Questions == "none" && before.Settings.QuestionCount == after.Settings.QuestionCount && AppendRequirementsEqual(before, after))
        {
            // Labels are metadata, but still appear in stage fingerprints. Preserve each current question without adopting stale siblings.
            var check = TaskDocumentValidator.ValidateDraft(oldInput, document, oldCheck);
            if (check.Errors.Count == 0)
                questions = questions.Select((q, i) => q.Acceptance is { } acceptance &&
                    !check.Diagnostics.Keys.Any(k => k.StartsWith($"questions[{i}]", StringComparison.Ordinal))
                    ? q with { Acceptance = acceptance with { InputFingerprint = fingerprint } } : q).ToArray();
        }
        return aligned with { Materials = materials, Questions = questions };
    }

    internal static bool AppendRequirementsEqual(LearningPlan before, LearningPlan after) =>
        Equal(before with
        {
            Name = after.Name,
            Settings = before.Settings! with { QuestionCount = after.Settings!.QuestionCount },
            Materials = after.Materials,
            Questions = before.Questions with { Formats = before.Questions.Formats.Order(StringComparer.Ordinal).ToArray() }
        },
            after with { Questions = after.Questions with { Formats = after.Questions.Formats.Order(StringComparer.Ordinal).ToArray() } }) &&
        MaterialRequirementsEqual(before.Materials, after.Materials);

    private static bool MaterialRequirementsEqual(MaterialDefinition before, MaterialDefinition after) =>
        Equal(before with { Label = after.Label }, after);

    private static bool MaterialRequirementsEqual(MaterialDefinition[] before, MaterialDefinition[] after)
    {
        if (before.Length != after.Length) return false;
        for (var i = 0; i < before.Length; i++)
            if (!MaterialRequirementsEqual(before[i], after[i])) return false;
        return true;
    }

    private static bool Equal<T>(T left, T right) => JsonSerializer.Serialize(left, EngineJson.Options) == JsonSerializer.Serialize(right, EngineJson.Options);

    private static string? RebuildInstruction(RevisionChange change, bool full, TaskDocument current)
    {
        if (!full) return change.Questions.Instruction;
        var instructions = new List<string>();
        if (change.Questions.Instruction is { } instruction) instructions.Add(instruction);
        foreach (var edit in change.Questions.Items) instructions.Add($"Question {edit.Id}: {edit.Instruction}");
        if (change.QuestionOrder is { } order)
            instructions.Add("Retain requested question content in this order where compatible with updated requirements: " + string.Join(", ", order) +
                ". Remove content from omitted questions: " + string.Join(", ", current.Questions.Select(q => q.Id).Except(order)));
        return instructions.Count == 0 ? null : string.Join("\n", instructions);
    }
}
