using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using static FamilyLearning.Api.TaskEngine.Validation.EngineValidation;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Applies editable fields while owning identity, source revisions and acceptance associations.</summary>
internal static class ActivityDraftChanges
{
    internal static TaskDocument Apply(ResolvedTaskRequest before, ResolvedTaskRequest request, TaskDocument current, EditableActivity edit)
    {
        var errors = new Dictionary<string, string[]>();
        if (edit is null || edit.Materials is not { Length: <= MaxMaterials } || edit.Questions is not { Length: <= MaxQuestionCount })
            throw Invalid("document", "יש לציין תוכן פעילות בגודל נתמך.");
        var materialIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in edit.Materials)
            if (item is null || !materialIds.Add(item.Id) || !request.Materials.Any(m => m.Id == item.Id))
                errors.AddError("materials", "יש לציין חומרים בעלי מזהים מוכרים וייחודיים.");
        if (errors.Count > 0) throw new TaskValidationException(errors);

        var fingerprint = TaskRequestResolver.Fingerprint(request);
        var materials = new List<MaterialContent>(request.Materials.Length);
        foreach (var requirement in request.Materials)
        {
            var priorRequirement = before.Materials.FirstOrDefault(m => m.Id == requirement.Id);
            if (priorRequirement is not null && priorRequirement.Source != requirement.Source)
                throw Invalid("materials", "שינוי סוג המקור דורש חומר חדש בעל מזהה חדש.");
            var prior = current.Materials.FirstOrDefault(m => m.Id == requirement.Id);
            var item = edit.Materials.FirstOrDefault(m => m.Id == requirement.Id);
            if (requirement.Source != "generated")
            {
                // A source replacement changes plan/input atomically. A saved old echo is not a second source authority.
                if (item is not null && (item.Title is not null || item.Body != requirement.Text &&
                    !(priorRequirement?.Text != requirement.Text && item.Body == prior?.Body)))
                    throw Invalid("materials", "יש לשנות מקור בתכנית או בקלט, ולא בגוף החומר.");
                var revision = prior is null ? 1 : prior.Body == requirement.Text ? prior.Revision : checked(prior.Revision + 1);
                materials.Add(new(requirement.Id, revision, null, requirement.Text!, new("supplied"), null));
            }
            else if (item is not null)
            {
                var changed = prior is null || prior.Title != item.Title || prior.Body != item.Body;
                materials.Add(new(item.Id, prior is null ? 1 : changed ? checked(prior.Revision + 1) : prior.Revision,
                    item.Title, item.Body, prior?.Origin ?? new("manual"),
                    changed ? new(fingerprint, []) : prior!.Acceptance));
            }
        }
        var sources = materials.Select(m => new MaterialRevision(m.Id, m.Revision)).ToArray();
        var used = request.Materials.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var previousQuestions = current.Questions.ToDictionary(q => q.Id, StringComparer.Ordinal);
        var questions = new List<DocumentQuestion>(edit.Questions.Length);
        foreach (var item in edit.Questions)
        {
            if (item is null) throw Invalid("questions", "שאלה אינה יכולה להיות null.");
            if (item.Interaction is null) throw Invalid("questions", "יש לציין סוג שאלה נתמך.");
            DocumentQuestion? prior = null;
            if (item.Id is not null && (!previousQuestions.TryGetValue(item.Id, out prior) || !used.Add(item.Id)))
                throw Invalid("questions", "מזהה השאלה אינו מוכר או מופיע יותר מפעם אחת.");
            var id = item.Id ?? Guid.NewGuid().ToString("N");
            var changed = prior is null || prior.Prompt != item.Prompt || prior.Answer != item.Answer || prior.Points != item.Points ||
                prior.Interaction.Type != item.Interaction.Type || !SameOptions(prior.Interaction.Options, item.Interaction.Options);
            var acceptance = changed ? new ContentAcceptance(fingerprint, sources.ToArray()) : prior!.Acceptance;
            // A removed source can later return with revision 1. Its old acceptance must not silently become current again.
            if (acceptance?.Sources.Any(source => !materials.Any(m => m.Id == source.Id)) == true) acceptance = null;
            questions.Add(new(id, item.Prompt, item.Interaction, item.Answer, item.Points, prior?.Origin ?? new("manual"),
                acceptance));
        }
        var document = new TaskDocument(edit.Title, edit.Instructions, materials.ToArray(), questions.ToArray());
        errors = TaskDocumentValidator.ValidateDraft(request, document).Errors;
        if (errors.Count > 0) throw new TaskValidationException(errors);
        return document;
    }

    private static bool SameOptions(string[]? left, string[]? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);

    private static TaskValidationException Invalid(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}
