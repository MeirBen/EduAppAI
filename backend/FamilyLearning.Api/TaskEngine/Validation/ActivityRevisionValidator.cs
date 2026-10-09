using FamilyLearning.Api.TaskEngine.Models;
using static FamilyLearning.Api.TaskEngine.Validation.EngineValidation;

namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Validates untrusted planner outcomes independently of provider schema enforcement.</summary>
public static class ActivityRevisionValidator
{
    public static void ValidateInput(ActivityRevisionInput input)
    {
        var errors = LearningPlanValidator.Validate(input.Plan);
        if (!HasText(input.Message, MessageLength)) errors.AddError("message", "יש לכתוב בקשה באורך נתמך.");
        if (input.Target is { } target && !(target.Kind == "material" && input.Plan.Materials.Any(m => m.Id == target.Id) ||
            target.Kind == "question" && input.Current.Questions.Any(q => q.Id == target.Id)))
            errors.AddError("target", "יש לבחור פריט קיים.");
        if (input.Sources is { } sources && (sources.Length > MaxMaterials ||
            sources.Any(s => s is null || !HasText(s.Label, NameLength) || !HasText(s.Text, BodyLimit))))
            errors.AddError("sources", "יש לציין מקורות מאושרים בגודל נתמך.");
        if (input.Context is { } turns && (turns.Length > MaxContextTurns || turns.Any(t => t is null ||
            t.Role is not ("parent" or "assistant") || !HasText(t.Text, MessageLength)) ||
            turns.Sum(t => (long)t.Text.Length) > ContextLength))
            errors.AddError("context", "היסטוריית השיחה גדולה מדי או אינה תקינה.");
        if (errors.Count > 0) throw new TaskValidationException(errors);
        errors = TaskDocumentValidator.ValidateDraft(TaskRequestResolver.ResolveOrThrow(input.Plan), input.Current).Errors;
        if (errors.Count > 0) throw new TaskValidationException(errors);
    }

    public static RevisionDecision Validate(RevisionDecision? decision, ActivityRevisionInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (decision is null || (decision.Answer is not null ? 1 : 0) + (decision.Clarification is not null ? 1 : 0) +
            (decision.Change is not null ? 1 : 0) != 1 ||
            decision.Answer is { } answer && !HasText(answer, RevisionReplyLength) ||
            decision.Clarification is { } clarification && !HasText(clarification, RevisionReplyLength))
            throw new TaskValidationException("result", "יש להחזיר תשובה, הבהרה או שינוי אחד בלבד.");
        if (decision.Change is not { } change) return decision;
        if (change.Plan is null) throw new TaskValidationException("plan", "יש להחזיר תכנית מלאה.");
        var plan = PlanChanges.AssignNewIds(change.Plan, input.Plan);
        if (change.Assumptions is not { Length: <= MaxAssumptions } || change.Assumptions.Any(a => !HasText(a, AssumptionLength)))
            errors.AddError("assumptions", "ההנחות אינן תקינות.");
        var existingIds = input.Plan.Materials.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var material in plan.Materials)
            if (!existingIds.Contains(material.Id) && material.Source == "supplied" &&
                !(input.Sources ?? []).Any(s => s.Text == material.Text))
                errors.AddError("sources", "יש לאשר מקור חדש לפני שינוי הפעילות.");
        ValidateEdits(change.MaterialEdits, MaxMaterials, id =>
            input.Current.Materials.Any(m => m.Id == id) && input.Plan.Materials.Any(m => m.Id == id && m.Source == "generated") &&
            plan.Materials.Any(m => m.Id == id && m.Source == "generated"), "materialEdits", errors);
        var questions = change.Questions;
        if (questions is null) errors.AddError("questions", "יש לציין היקף שינוי לשאלות.");
        else
        {
            if (questions.Scope is not ("none" or "selected" or "append" or "all") || questions.Items is null ||
                (questions.Scope == "selected" ? questions.Items.Length is < 1 or > MaxSelectedEdits : questions.Items.Length != 0) ||
                (questions.Scope is "none" or "selected" ? questions.Instruction is not null : questions.Instruction is { } instruction && !HasText(instruction, MessageLength)))
                errors.AddError("questions", "היקף שינוי השאלות אינו תקין.");
            ValidateEdits(questions.Items, MaxSelectedEdits, id => input.Current.Questions.Any(q => q.Id == id), "questions.items", errors);
            if (questions.Scope == "append" && (plan.Settings!.QuestionCount <= input.Plan.Settings!.QuestionCount ||
                change.MaterialEdits is not { Length: 0 } || change.QuestionOrder is not null ||
                !RevisionScope.AppendRequirementsEqual(input.Plan, plan)))
                errors.AddError("questions", "הוספת שאלות בלבד דורשת הגדלת כמות ללא שינוי בדרישות האחרות.");
        }
        if (change.QuestionOrder is { } order && (order.Length is < 1 or > MaxQuestionCount ||
            order.Distinct(StringComparer.Ordinal).Count() != order.Length ||
            order.Any(id => !input.Current.Questions.Any(q => q.Id == id)) ||
            plan.Settings!.QuestionCount != order.Length || questions?.Scope != "none"))
            errors.AddError("questionOrder", "יש לציין שאלות קיימות וייחודיות לפי הכמות המבוקשת.");
        if (!HasGeneratedContent(input.Plan, input.Current) && (change.MaterialEdits is not { Length: 0 } ||
            questions?.Scope != "none" || change.QuestionOrder is not null))
            errors.AddError("change", "לפני היצירה יש לשנות רק את הדרישות והמקורות המאושרים.");
        if (errors.Count > 0) throw new TaskValidationException(errors);
        return decision with { Change = change with { Plan = plan } };
    }

    internal static bool HasGeneratedContent(LearningPlan plan, TaskDocument document) => document.Questions.Length > 0 ||
        document.Materials.Any(m => plan.Materials.Any(r => r.Id == m.Id && r.Source == "generated"));

    private static void ValidateEdits(ContentEdit[]? edits, int maximum, Func<string, bool> target,
        string path, Dictionary<string, string[]> errors)
    {
        if (edits is null || edits.Length > maximum || edits.Any(e => e is null || !HasText(e.Instruction, EditInstructionLength) || !target(e.Id)) ||
            edits.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != edits.Length)
            errors.AddError(path, "יש לבחור פריטים קיימים ושונים והנחיות באורך נתמך.");
    }
}
