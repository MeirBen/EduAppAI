using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using static FamilyLearning.Api.TaskEngine.Validation.EngineValidation;

namespace FamilyLearning.Api.TaskEngine;

/// <summary>Pure source assembly and atomic candidate acceptance; persistence and workflow transitions belong to features.</summary>
/// <remarks>Requests must be resolved and current documents must pass <see cref="TaskDocumentValidator.ValidateDraft(ResolvedTaskRequest, TaskDocument)"/>
/// without unsafe-input errors. Repairable draft diagnostics may remain.</remarks>
public static class TaskAssembly
{
    /// <summary>Applies only a complete valid generated batch, restoring supplied originals and plan order.</summary>
    public static MaterialAcceptance AcceptMaterials(ResolvedTaskRequest request, TaskDocument current, MaterialCandidateBatch candidate)
    {
        var errors = new Dictionary<string, string[]>();
        var fingerprint = TaskRequestResolver.Fingerprint(request);
        var expected = request.Materials.Where(m => m.Source == "generated" && NeedsMaterial(current, m.Id, fingerprint)).ToArray();
        if (candidate is null)
        {
            errors.AddError("candidate", "יש לציין חומרים שנוצרו.");
            throw new TaskValidationException(errors);
        }
        candidate = candidate with { Materials = candidate.Materials?.ToArray()! };
        if (candidate.Materials is not { Length: <= 4 } items || items.Length != expected.Length)
        {
            errors.AddError("materials", "רשימת החומרים שנוצרו אינה תואמת לדרישה.");
            return new(null, candidate, errors);
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
            if (item is null || !ids.Add(item.Id) || !expected.Any(m => m.Id == item.Id) ||
                !HasText(item.Body, BodyLimit) || item.Title is { Length: > 100 })
                errors.AddError("materials", "החומרים שנוצרו כוללים מזהה או תוכן לא תקינים.");
        if (errors.Count > 0) return new(null, candidate, errors);
        var existingMaterials = RestoreSources(request, current.Materials);
        var materials = new List<MaterialContent>(request.Materials.Length);
        foreach (var requirement in request.Materials)
        {
            var item = items.FirstOrDefault(m => m.Id == requirement.Id);
            var previous = existingMaterials.FirstOrDefault(m => m.Id == requirement.Id);
            materials.Add(item is null ? previous! : new(item.Id, checked((previous?.Revision ?? 0) + 1), item.Title, item.Body,
                new("generated", request.EngineRevision, fingerprint), new(fingerprint, [])));
        }
        var document = current with { Materials = materials.ToArray(), Questions = CopyQuestions(current.Questions) };
        var materialCheck = TaskDocumentValidator.ValidateMaterials(request, document);
        var check = TaskDocumentValidator.ValidateDraft(request, document, materialCheck);
        foreach (var error in check.Errors) errors.AddError(error.Key, error.Value[0]);
        foreach (var diagnostic in materialCheck.Diagnostics) errors.AddError(diagnostic.Key, diagnostic.Value[0]);
        return new(errors.Count == 0 ? document : null, candidate, errors);
    }

    /// <summary>Validates the entire batch before allocating question IDs; preserves accepted materials.</summary>
    public static TaskDocument AcceptQuestions(ResolvedTaskRequest request, TaskDocument current, QuestionCandidateBatch candidate)
    {
        var document = current with { Materials = RestoreSources(request, current.Materials) };
        RequireMaterials(TaskDocumentValidator.ValidateMaterials(request, document));
        var errors = TaskDocumentValidator.ValidateQuestionBatch(request, document, candidate);
        if (errors.Count > 0) throw new TaskValidationException(errors);
        var fingerprint = TaskRequestResolver.Fingerprint(request);
        var sources = document.Materials.Select(m => new MaterialRevision(m.Id, m.Revision)).ToArray();
        // No IDs are allocated until the entire untrusted batch and its assembled size have passed.
        var questions = candidate.Questions.Select(q => new DocumentQuestion(Guid.NewGuid().ToString("N"), q.Prompt,
            CopyInteraction(q.Interaction), q.Answer, q.Points, new("generated", request.EngineRevision, fingerprint),
            new(fingerprint, sources.ToArray()))).ToArray();
        return document with { Title = candidate.Title, Instructions = candidate.Instructions, Questions = questions };
    }

    /// <summary>Updates selected acceptance bases after strict checks, preserving original generation provenance.</summary>
    public static TaskDocument Adopt(ResolvedTaskRequest request, TaskDocument current, string[] materialIds, string[] questionIds, DateTime adoptedAtUtc)
    {
        var errors = new Dictionary<string, string[]>();
        var selectedMaterials = materialIds.ToHashSet(StringComparer.Ordinal);
        var selectedQuestions = questionIds.ToHashSet(StringComparer.Ordinal);
        var existingQuestions = current.Questions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        if (adoptedAtUtc.Kind != DateTimeKind.Utc) errors.AddError("adoptedAtUtc", "זמן האימוץ חייב להיות UTC.");
        if (selectedMaterials.Count != materialIds.Length ||
            selectedMaterials.Any(id => !request.Materials.Any(m => m.Id == id && m.Source == "generated") || !current.Materials.Any(m => m.Id == id)))
            errors.AddError("materials", "יש לבחור חומרים קיימים שנוצרו.");
        if (selectedQuestions.Count != questionIds.Length || !selectedQuestions.IsSubsetOf(existingQuestions))
            errors.AddError("questions", "יש לבחור שאלות קיימות.");
        if (errors.Count > 0) throw new TaskValidationException(errors);
        var fingerprint = TaskRequestResolver.Fingerprint(request);
        var document = current with
        {
            Materials = current.Materials.Select(m => m with
            {
                Acceptance = selectedMaterials.Contains(m.Id)
                    ? new(fingerprint, [], adoptedAtUtc) : CopyAcceptance(m.Acceptance)
            }).ToArray()
        };
        var materialCheck = TaskDocumentValidator.ValidateMaterials(request, document);
        if (selectedQuestions.Count > 0) RequireMaterials(materialCheck);
        var sources = document.Materials.Select(m => new MaterialRevision(m.Id, m.Revision)).ToArray();
        document = document with
        {
            Questions = current.Questions.Select(q => q with
            {
                Interaction = CopyInteraction(q.Interaction),
                Acceptance = selectedQuestions.Contains(q.Id)
                    ? new(fingerprint, sources.ToArray(), adoptedAtUtc) : CopyAcceptance(q.Acceptance)
            }).ToArray()
        };
        errors = TaskDocumentValidator.ValidateAdoption(request, document, selectedMaterials, selectedQuestions, materialCheck);
        if (errors.Count > 0) throw new TaskValidationException(errors);
        return document;
    }

    private static bool NeedsMaterial(TaskDocument current, string id, string fingerprint) =>
        current.Materials.FirstOrDefault(m => m.Id == id)?.Acceptance?.InputFingerprint != fingerprint;

    private static void RequireMaterials(DraftDocumentCheck check)
    {
        // Question work may replace an incomplete question batch, but every material must already be current and strict-valid.
        foreach (var diagnostic in check.Diagnostics) check.Errors.AddError(diagnostic.Key, diagnostic.Value[0]);
        if (check.Errors.Count > 0) throw new TaskValidationException(check.Errors);
    }

    private static MaterialContent[] RestoreSources(ResolvedTaskRequest request, MaterialContent[] current)
    {
        var materials = new List<MaterialContent>(request.Materials.Length);
        foreach (var requirement in request.Materials)
        {
            var previous = current.FirstOrDefault(m => m.Id == requirement.Id);
            if (requirement.Source == "generated")
            {
                if (previous is not null) materials.Add(previous with { Acceptance = CopyAcceptance(previous.Acceptance) });
                continue;
            }
            materials.Add(previous is { Origin.Kind: "supplied", Title: null } && previous.Body == requirement.Text
                ? previous with { Acceptance = CopyAcceptance(previous.Acceptance) }
                : new(requirement.Id, checked((previous?.Revision ?? 0) + 1), null, requirement.Text!, new("supplied"), null));
        }
        return materials.ToArray();
    }

    // Immutable record members may be shared; detach the arrays that a caller can still mutate.
    private static ContentAcceptance? CopyAcceptance(ContentAcceptance? acceptance) =>
        acceptance is null ? null : acceptance with { Sources = acceptance.Sources.ToArray() };

    private static DocumentQuestion[] CopyQuestions(DocumentQuestion[] questions) => questions.Select(question => question with
    {
        Interaction = CopyInteraction(question.Interaction),
        Acceptance = CopyAcceptance(question.Acceptance)
    }).ToArray();

    private static QuestionInteraction CopyInteraction(QuestionInteraction interaction) =>
        interaction with { Options = interaction.Options?.ToArray() };
}
