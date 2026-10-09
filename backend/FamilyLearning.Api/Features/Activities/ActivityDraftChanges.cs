using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using static FamilyLearning.Api.TaskEngine.Validation.EngineValidation;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Applies editable fields while owning identity, source revisions and acceptance associations.</summary>
internal static class ActivityDraftChanges
{
    private static LearningPlan ValidateManualSave(LearningPlan plan, TaskDocument current, SaveActivityRequest body)
    {
        var replacements = body.SourceReplacements ?? [];
        if (replacements.Length > MaxMaterials || replacements.Any(r => r is null || !HasText(r.Text, BodyLimit) ||
            !plan.Materials.Any(m => m.Id == r.Id && m.Source == "supplied")) ||
            replacements.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != replacements.Length)
            throw new TaskValidationException("sourceReplacements", "יש לאשר החלפה של מקור קיים ובגודל נתמך.");
        var allowed = plan with
        {
            Materials = plan.Materials.Select(m =>
            replacements.FirstOrDefault(r => r.Id == m.Id) is { } replacement ? m with { Text = replacement.Text } : m).ToArray()
        };
        // Formats are a set; form projection order must not block content edits or replace the saved order.
        if (body.Plan?.Questions?.Formats is not { } formats || formats.Length != allowed.Questions.Formats.Length ||
            !formats.Order(StringComparer.Ordinal).SequenceEqual(allowed.Questions.Formats.Order(StringComparer.Ordinal)) ||
            StoredJson.Write(allowed) != StoredJson.Write(
                body.Plan with { Questions = body.Plan.Questions with { Formats = allowed.Questions.Formats } }))
            throw new TaskValidationException("plan", "שינוי דרישות ומבנה הפעילות נעשה דרך השיחה.");
        if (body.Document?.Materials is null || body.Document.Questions is null ||
            !body.Document.Materials.Select(m => m?.Id).SequenceEqual(current.Materials.Select(m => m.Id)) ||
            !body.Document.Questions.Select(q => q?.Id).SequenceEqual(current.Questions.Select(q => q.Id)) ||
            body.Document.Questions.Where((q, i) => q?.Interaction is null || q.Interaction.Type != current.Questions[i].Interaction.Type ||
                (q.Interaction.Options?.Length ?? 0) != (current.Questions[i].Interaction.Options?.Length ?? 0)).Any())
            throw new TaskValidationException("document", "עריכה ידנית שומרת על הפריטים, הסדר וסוגי השאלות. שינוי המבנה נעשה דרך השיחה.");
        return allowed;
    }

    internal static (LearningPlan Plan, TaskDocument Document) Apply(ActivityDraft draft, SaveActivityRequest body)
    {
        var current = draft.Document;
        var plan = ValidateManualSave(draft.Plan, current, body);
        var request = TaskRequestResolver.ResolveOrThrow(plan);
        var edit = body.Document;
        var fingerprint = TaskRequestResolver.Fingerprint(request);
        var materials = new MaterialContent[current.Materials.Length];
        for (var i = 0; i < materials.Length; i++)
        {
            var prior = current.Materials[i];
            var item = edit.Materials[i];
            var requirement = request.Materials.Single(m => m.Id == prior.Id);
            var supplied = requirement.Source == "supplied";
            if (supplied && (item.Title is not null || item.Body != requirement.Text))
                throw new TaskValidationException("materials", "יש לאשר החלפת מקור, ולא לשנות את גוף החומר ישירות.");
            var changed = prior.Title != item.Title || prior.Body != item.Body;
            materials[i] = prior with
            {
                Title = item.Title,
                Body = item.Body,
                Revision = changed ? checked(prior.Revision + 1) : prior.Revision,
                Acceptance = supplied ? null : changed ? new(fingerprint, []) : prior.Acceptance
            };
        }
        var sources = materials.Select(m => new MaterialRevision(m.Id, m.Revision)).ToArray();
        var questions = new DocumentQuestion[current.Questions.Length];
        for (var i = 0; i < questions.Length; i++)
        {
            var prior = current.Questions[i];
            var item = edit.Questions[i];
            var changed = prior.Prompt != item.Prompt || prior.Answer != item.Answer || prior.Points != item.Points ||
                !SameOptions(prior.Interaction.Options, item.Interaction.Options);
            questions[i] = prior with
            {
                Prompt = item.Prompt,
                Interaction = item.Interaction,
                Answer = item.Answer,
                Points = item.Points,
                Acceptance = changed ? new(fingerprint, sources.ToArray()) : prior.Acceptance
            };
        }
        var document = new TaskDocument(edit.Title, edit.Instructions, materials, questions);
        var errors = TaskDocumentValidator.ValidateDraft(request, document).Errors;
        if (errors.Count > 0) throw new TaskValidationException(errors);
        return (plan, document);
    }

    private static bool SameOptions(string[]? left, string[]? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);
}
