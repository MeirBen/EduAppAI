using FamilyLearning.Api.TaskEngine.Models;
using static FamilyLearning.Api.TaskEngine.Validation.EngineValidation;

namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Owns safe draft shape and strict deterministic release/candidate checks.</summary>
public static class TaskDocumentValidator
{
    /// <summary>Incomplete safe content is retained with bounded diagnostics; unsafe content returns Errors.</summary>
    public static DraftDocumentCheck ValidateDraft(ResolvedTaskRequest request, TaskDocument document) =>
        ValidateDraft(request, document, null);

    // Reuse the material check when assembly already needed it for stage acceptance.
    internal static DraftDocumentCheck ValidateDraft(ResolvedTaskRequest request, TaskDocument document, DraftDocumentCheck? materialCheck)
    {
        var errors = new Dictionary<string, string[]>();
        var diagnostics = new Dictionary<string, string[]>();
        if (document is null)
        {
            errors.AddError("document", "יש לציין תוכן פעילות.");
            return new(errors, diagnostics);
        }
        ValidateText(document.Title, 100, "title", errors, diagnostics);
        if (document.Instructions?.Length > 1000) errors.AddError("instructions", "ההנחיות מוגבלות ל־1,000 תווים.");
        long length = (long)(document.Title?.Length ?? 0) + (document.Instructions?.Length ?? 0);
        var fingerprint = TaskRequestResolver.Fingerprint(request);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        materialCheck ??= ValidateMaterials(request, document);
        foreach (var error in materialCheck.Errors) errors.AddError(error.Key, error.Value[0]);
        foreach (var diagnostic in materialCheck.Diagnostics) diagnostics.AddError(diagnostic.Key, diagnostic.Value[0]);
        if (document.Materials is { Length: <= 4 })
            foreach (var material in document.Materials)
            {
                if (material is null) continue;
                ids.Add(material.Id);
                length += (long)(material.Title?.Length ?? 0) + (material.Body?.Length ?? 0);
            }
        if (document.Questions is not { Length: <= MaxQuestionCount }) errors.AddError("questions", "רשימת השאלות גדולה מדי או חסרה.");
        else
        {
            if (document.Questions.Length != request.Settings.QuestionCount) diagnostics.AddError("questions", "מספר השאלות אינו תואם לדרישה.");
            var seenFormats = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < document.Questions.Length; i++)
            {
                var question = document.Questions[i];
                var path = $"questions[{i}]";
                if (question is null)
                {
                    errors.AddError(path, "שאלה אינה יכולה להיות null.");
                    continue;
                }
                ValidateId(question.Id, path, ids, errors);
                ValidateQuestion(question.Prompt, question.Interaction, question.Answer, question.Points,
                    request.Questions, path, errors, diagnostics);
                length += QuestionLength(question.Prompt, question.Interaction, question.Answer);
                if (question.Interaction is { } interaction) seenFormats.Add(interaction.Type);
                ValidateEvidence(question.Origin, question.Acceptance, path, errors);
                if (!IsCurrent(question.Acceptance, fingerprint, document.Materials))
                    diagnostics.AddError(path + ".stale", "השאלה דורשת יצירה מחדש או אימוץ תחת הדרישות והמקורות הנוכחיים.");
            }
            if (request.Questions.Formats.Any(f => !seenFormats.Contains(f))) diagnostics.AddError("questions.formats", "חסרים סוגי שאלות שהתבקשו.");
        }
        if (length > ContentLimit) errors.AddError("document", ContentLimitError);
        return new(errors, diagnostics);
    }

    /// <summary>Checks complete content, strict measurements and current acceptance; features add review and operation guards.</summary>
    public static Dictionary<string, string[]> ValidateRelease(ResolvedTaskRequest request, TaskDocument document)
    {
        var check = ValidateDraft(request, document);
        foreach (var error in check.Diagnostics) check.Errors.AddError(error.Key, error.Value[0]);
        return check.Errors;
    }

    /// <summary>Checks material readiness independently of incomplete questions and their bounded display diagnostics.</summary>
    internal static DraftDocumentCheck ValidateMaterials(ResolvedTaskRequest request, TaskDocument document)
    {
        var errors = new Dictionary<string, string[]>();
        var diagnostics = new Dictionary<string, string[]>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var fingerprint = TaskRequestResolver.Fingerprint(request);
        long length = 0;
        if (document.Materials is not { Length: <= 4 }) errors.AddError("materials", "יש לציין עד ארבעה חומרים.");
        else
        {
            foreach (var material in document.Materials)
            {
                if (material is null)
                {
                    errors.AddError("materials", "חומר אינו יכול להיות null.");
                    continue;
                }
                var path = $"materials.{material.Id}";
                ValidateId(material.Id, "materials", ids, errors);
                var expected = request.Materials.FirstOrDefault(m => m.Id == material.Id);
                if (expected is null) errors.AddError("materials", "מזהה חומר אינו מוכר.");
                if (material.Revision < 1) errors.AddError(path + ".revision", "גרסת החומר אינה תקינה.");
                if (material.Title?.Length > 100) errors.AddError(path + ".title", "כותרת מוגבלת ל־100 תווים.");
                ValidateText(material.Body, BodyLimit, path + ".body", errors, diagnostics);
                length += (long)(material.Title?.Length ?? 0) + (material.Body?.Length ?? 0);
                ValidateEvidence(material.Origin, material.Acceptance, path, errors);
                if (expected is { Source: not "generated" })
                {
                    if (material.Body != expected.Text || material.Title is not null || material.Origin?.Kind != "supplied")
                        errors.AddError(path, "יש לשמור את טקסט המקור בדיוק כפי שאושר.");
                }
                else if (expected is not null && material.Acceptance?.InputFingerprint != fingerprint)
                    diagnostics.AddError(path + ".stale", "החומר דורש יצירה מחדש או אימוץ תחת הדרישות הנוכחיות.");
            }
            foreach (var expected in request.Materials)
                if (!ids.Contains(expected.Id)) diagnostics.AddError($"materials.{expected.Id}", "חסר חומר נדרש.");
            if (document.Materials.Length == request.Materials.Length &&
                !document.Materials.Select(m => m?.Id).SequenceEqual(request.Materials.Select(m => m.Id)))
                errors.AddError("materials", "סדר החומרים חייב להתאים לתכנית.");
        }
        if (length + 1 + MinimumQuestionLength(request.Settings.QuestionCount, request.Questions.Formats, request.Questions.ChoiceCount) > ContentLimit)
            diagnostics.AddError("materials.capacity", "לא נשאר מספיק מקום לחומרים ולשאלות המבוקשות.");
        if (errors.Count == 0)
            foreach (var measurement in TextLength.Measure(request, document))
                if (measurement.Satisfied == false) diagnostics.AddError($"length.{measurement.Scope}", "אורך החומר אינו עומד בדרישה המדויקת או בטווח.");
        return new(errors, diagnostics);
    }

    /// <summary>Checks selected adoption targets directly, so diagnostic truncation cannot hide a blocking target error.</summary>
    internal static Dictionary<string, string[]> ValidateAdoption(ResolvedTaskRequest request, TaskDocument document,
        HashSet<string> materialIds, HashSet<string> questionIds, DraftDocumentCheck materialCheck)
    {
        var errors = ValidateDraft(request, document, materialCheck).Errors;
        if (errors.Count > 0) return errors;
        foreach (var diagnostic in materialCheck.Diagnostics)
            if ((materialIds.Count > 0 && diagnostic.Key is "length.total" or "materials.capacity") || materialIds.Any(id =>
                diagnostic.Key.StartsWith($"materials.{id}", StringComparison.Ordinal) || diagnostic.Key == $"length.{id}"))
                errors.AddError(diagnostic.Key, diagnostic.Value[0]);
        for (var i = 0; i < document.Questions.Length; i++)
        {
            var question = document.Questions[i];
            if (!questionIds.Contains(question.Id)) continue;
            ValidateQuestion(question.Prompt, question.Interaction, question.Answer, question.Points, request.Questions,
                $"questions[{i}]", errors, errors);
        }
        return errors;
    }

    internal static Dictionary<string, string[]> ValidateQuestionBatch(ResolvedTaskRequest request, TaskDocument current, QuestionCandidateBatch candidate)
    {
        var errors = new Dictionary<string, string[]>();
        if (candidate is null)
        {
            errors.AddError("candidate", "יש לציין שאלות שנוצרו.");
            return errors;
        }
        ValidateText(candidate.Title, 100, "title", errors, errors);
        if (candidate.Instructions?.Length > 1000) errors.AddError("instructions", "ההנחיות מוגבלות ל־1,000 תווים.");
        if (candidate.Questions is not { Length: <= MaxQuestionCount } questions || questions.Length != request.Settings.QuestionCount)
        {
            errors.AddError("questions", "מספר השאלות אינו תואם לדרישה.");
            return errors;
        }
        long length = (long)(candidate.Title?.Length ?? 0) + (candidate.Instructions?.Length ?? 0);
        foreach (var material in current.Materials) length += (long)(material.Title?.Length ?? 0) + material.Body.Length;
        var formats = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < questions.Length; i++)
        {
            var question = questions[i];
            if (question is null)
            {
                errors.AddError($"questions[{i}]", "שאלה אינה יכולה להיות null.");
                continue;
            }
            ValidateQuestion(question.Prompt, question.Interaction, question.Answer, question.Points, request.Questions,
                $"questions[{i}]", errors, errors);
            length += QuestionLength(question.Prompt, question.Interaction, question.Answer);
            if (question.Interaction is { } interaction) formats.Add(interaction.Type);
        }
        if (request.Questions.Formats.Any(f => !formats.Contains(f))) errors.AddError("questions.formats", "חסרים סוגי שאלות שהתבקשו.");
        if (length > ContentLimit) errors.AddError("document", ContentLimitError);
        return errors;
    }

    private static bool IsCurrent(ContentAcceptance? acceptance, string fingerprint, MaterialContent[]? materials) =>
        acceptance is not null && acceptance.InputFingerprint == fingerprint && acceptance.Sources is not null && materials is not null &&
        acceptance.Sources.Length == materials.Length && acceptance.Sources.Select(s => s?.Id).Distinct(StringComparer.Ordinal).Count() == materials.Length &&
        acceptance.Sources.All(source => source is not null && materials.Any(m => m?.Id == source.Id && m.Revision == source.Revision));

    private static void ValidateQuestion(string? prompt, QuestionInteraction? interaction, QuestionAnswer? answer, int points,
        ResolvedQuestions requirements, string path, Dictionary<string, string[]> errors, Dictionary<string, string[]> diagnostics)
    {
        ValidateText(prompt, 500, path + ".prompt", errors, diagnostics);
        if (points is < 0 or > 100) errors.AddError(path + ".points", "הניקוד חייב להיות בין 0 ל־100.");
        if (answer?.Value is { Length: > 200 }) errors.AddError(path + ".answer", "תשובה מוגבלת ל־200 תווים.");
        if (interaction is null || !IsFormat(interaction.Type))
        {
            errors.AddError(path + ".interaction", "סוג השאלה אינו נתמך.");
            return;
        }
        if (!requirements.Formats.Contains(interaction.Type)) diagnostics.AddError(path + ".format", "סוג השאלה אינו תואם לתכנית.");
        if (interaction.Options is { } options)
        {
            if (interaction.Type != "single-choice" || options.Length > MaxChoiceCount || options.Any(o => o is null || o.Length > 200))
                errors.AddError(path + ".options", "אפשרויות התשובה אינן בטווח הנתמך.");
        }
        if (interaction.Type == "single-choice" &&
            (!QuestionRules.ValidOptions(interaction.Options) || interaction.Options!.Length != requirements.ChoiceCount))
            diagnostics.AddError(path + ".options", "יש להגדיר את מספר האפשרויות המבוקש, עם ערכים שונים ותקינים.");
        if (QuestionRules.AnswerError(interaction, answer) is { } error) diagnostics.AddError(path + ".answer", error);
    }

    private static void ValidateText(string? text, int max, string path,
        Dictionary<string, string[]> errors, Dictionary<string, string[]> diagnostics)
    {
        if (text is null || text.Length > max) errors.AddError(path, "הטקסט חסר או ארוך מדי.");
        else if (string.IsNullOrWhiteSpace(text)) diagnostics.AddError(path, "יש למלא תוכן בשדה הזה.");
    }

    private static long QuestionLength(string? prompt, QuestionInteraction? interaction, QuestionAnswer? answer)
    {
        long length = (long)(prompt?.Length ?? 0) + (answer?.Value?.Length ?? 0);
        if (interaction?.Options is { Length: <= MaxChoiceCount } options) foreach (var option in options) length += option?.Length ?? 0;
        return length;
    }

    private static void ValidateEvidence(ContentOrigin? origin, ContentAcceptance? acceptance, string path, Dictionary<string, string[]> errors)
    {
        if (origin is null || origin.Kind is not ("manual" or "generated" or "supplied")) errors.AddError(path + ".origin", "מקור התוכן אינו תקין.");
        if (acceptance is not null && (acceptance.InputFingerprint is not { Length: 64 } ||
            acceptance.Sources is not { Length: <= 4 } || acceptance.Sources.Any(s => s is null || !IsId(s.Id) || s.Revision < 1) ||
            acceptance.AdoptedAtUtc is { Kind: not DateTimeKind.Utc })) errors.AddError(path + ".acceptance", "פרטי קבלת התוכן אינם תקינים.");
    }
}
