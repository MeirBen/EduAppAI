using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class TaskAssemblyTests
{
    private static readonly TaskDocument Empty = new("", null, [], []);
    // Blank prompt, unplanned format, unrequested options and a missing answer, plus staleness under changed input:
    // five display diagnostics per question fill the bounded diagnostic list within the question cap.
    private static TaskDocument WithFiveDiagnosticsPerQuestion(TaskDocument document) => document with
    {
        Questions = document.Questions.Select(q => q with { Prompt = " ", Interaction = new("single-choice", ["א", "ב"]), Answer = null }).ToArray()
    };
    private static QuestionCandidate Question(string type = "numeric-input") => new("שאלה", new(type,
        type == "single-choice" ? ["א", "ב", "ג"] : null), new(type == "single-choice" ? "ב" : "1"), 1);
    private static QuestionCandidateBatch Questions(params QuestionCandidate[] questions) => new("כותרת", "הנחיות", questions);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Returned_content_does_not_share_mutable_state_with_the_current_draft(bool adopt)
    {
        var plan = Reading() with { Questions = Mixed(true).Questions };
        var request = Resolve(plan);
        var current = TaskAssembly.AcceptMaterials(request, Empty, new([new(MaterialId, null, "שלום עולם")])).Document!;
        current = TaskAssembly.AcceptQuestions(request, current, Questions(Question("single-choice"), Question("single-choice")));
        var changedInput = request with { Settings = request.Settings with { Topic = "נושא חדש" } };
        var result = adopt
            ? TaskAssembly.Adopt(changedInput, current, [MaterialId], current.Questions.Select(q => q.Id).ToArray(), DateTime.UtcNow)
            : TaskAssembly.AcceptMaterials(changedInput, current, new([new(MaterialId, null, "תוכן חדש")])).Document!;

        result.Questions[0].Interaction.Options![0] = "changed";
        result.Questions[0].Acceptance!.Sources[0] = new(MaterialId, 99);
        result.Materials[0] = result.Materials[0] with { Body = "changed" };

        Assert.Equal("א", current.Questions[0].Interaction.Options![0]);
        Assert.Equal(1, current.Questions[0].Acceptance!.Sources[0].Revision);
        Assert.Equal(1, result.Questions[1].Acceptance!.Sources[0].Revision);
        Assert.Equal("שלום עולם", current.Materials[0].Body);
        Assert.Empty(TaskDocumentValidator.ValidateRelease(request, current));
    }

    [Fact]
    public void Source_assembly_preserves_order_and_exact_text_and_rejects_provider_source_echo()
    {
        var plan = Reading() with { Materials = [Supplied().Materials[0], Reading().Materials[0] with { Id = OtherId }] };
        var request = Resolve(plan);
        var result = TaskAssembly.AcceptMaterials(request, Empty, new([new(OtherId, "כותרת", "תוכן חדש")]));
        var document = Assert.IsType<TaskDocument>(result.Document);
        Assert.Equal([MaterialId, OtherId], document.Materials.Select(m => m.Id));
        Assert.Equal(Source, document.Materials[0].Body);
        Assert.Empty(Empty.Materials);
        Assert.Null(TaskAssembly.AcceptMaterials(request, Empty, new([new(MaterialId, null, "changed"), new(OtherId, null, "text")])).Document);
        Assert.Null(TaskAssembly.AcceptMaterials(request, Empty, new([])).Document);
        Assert.Null(TaskAssembly.AcceptMaterials(request, Empty, new([new(OtherId, null, "a"), new(OtherId, null, "b")])).Document);
    }

    [Fact]
    public void Strict_material_mismatch_is_diagnostic_only_but_target_mismatch_is_accepted()
    {
        var plan = Reading();
        var candidate = new MaterialCandidateBatch([new(MaterialId, null, "שלום עולם")]);
        var request = Resolve(plan);
        Assert.NotNull(TaskAssembly.AcceptMaterials(request, Empty, candidate).Document);
        plan = plan with { Materials = [plan.Materials[0] with { Length = new("range", Lower: 120, Upper: 130) }] };
        var rejected = TaskAssembly.AcceptMaterials(Resolve(plan), Empty, candidate);
        Assert.Null(rejected.Document);
        Assert.NotEmpty(rejected.Diagnostics);
        Assert.Equal("שלום עולם", rejected.Candidate.Materials[0].Body);
        Assert.Empty(Empty.Materials);
    }

    [Fact]
    public void Question_batches_validate_count_formats_choices_and_answers_before_application()
    {
        var request = Resolve(Mixed());
        var valid = Questions(Question(), Question("text-input"), Question("single-choice"));
        var document = TaskAssembly.AcceptQuestions(request, Empty, valid);
        Assert.Empty(TaskDocumentValidator.ValidateRelease(request, document));
        Assert.Equal(3, document.Questions.Select(q => q.Id).Distinct().Count());
        Assert.All(document.Questions, q => Assert.Matches("^[0-9a-f]{32}$", q.Id));
        QuestionCandidateBatch[] invalid = [Questions(Question()), Questions(Question(), Question(), Question()),
            valid with { Questions = [Question(), Question("text-input"), Question("single-choice") with { Answer = new("missing") }] },
            valid with { Questions = [Question(), Question("text-input"), Question("single-choice") with { Interaction = new("single-choice", ["א", "ב"]) }] }];
        foreach (var candidate in invalid) Assert.Throws<TaskValidationException>(() => TaskAssembly.AcceptQuestions(request, Empty, candidate));
        Assert.Empty(Empty.Questions);
        valid.Questions[2].Interaction.Options![0] = "mutated";
        Assert.Equal("א", document.Questions[2].Interaction.Options![0]);
    }

    [Fact]
    public void Manual_incomplete_answers_save_as_diagnostics_without_selecting_a_new_answer()
    {
        var request = Resolve(Mixed(true));
        var document = TaskAssembly.AcceptQuestions(request, Empty, Questions(Question("single-choice"), Question("single-choice"), Question("single-choice")));
        var original = document.Questions[0];
        document.Questions[0] = original with { Interaction = new("single-choice", ["א", "ד", "ג"]) };
        document.Questions[1] = document.Questions[1] with { Answer = null };
        var check = TaskDocumentValidator.ValidateDraft(request, document);
        Assert.Empty(check.Errors);
        Assert.NotEmpty(check.Diagnostics);
        Assert.Equal("ב", document.Questions[0].Answer!.Value);
        Assert.NotEmpty(TaskDocumentValidator.ValidateRelease(request, document));
        Assert.Throws<TaskValidationException>(() => TaskAssembly.AcceptQuestions(request, Empty,
            Questions(Question("single-choice") with { Answer = null }, Question("single-choice"), Question("single-choice"))));
    }

    [Fact]
    public void Current_requirements_and_actual_source_revisions_determine_staleness()
    {
        var request = Resolve(Reading());
        var document = TaskAssembly.AcceptMaterials(request, Empty, new([new(MaterialId, null, "שלום עולם")])).Document!;
        document = TaskAssembly.AcceptQuestions(request, document, Questions(Question("text-input"), Question("text-input")));
        Assert.Empty(TaskDocumentValidator.ValidateRelease(request, document));
        Assert.Equal([new MaterialRevision(MaterialId, 1)], document.Questions[0].Acceptance!.Sources);
        var changedRevision = document with { Materials = [document.Materials[0] with { Revision = 2, Body = "עריכה" }] };
        Assert.Contains(TaskDocumentValidator.ValidateDraft(request, changedRevision).Diagnostics.Keys, key => key.Contains("stale"));
        var changedInput = request with { Settings = request.Settings with { Topic = "אחר" } };
        Assert.Contains(TaskDocumentValidator.ValidateDraft(changedInput, document).Diagnostics.Keys, key => key.Contains("stale"));
        Assert.Empty(TaskDocumentValidator.ValidateRelease(request with { EngineRevision = request.EngineRevision + 1 }, document));
        var origin = document.Questions[0].Origin;
        var adopted = TaskAssembly.Adopt(changedInput, document, [MaterialId], document.Questions.Select(q => q.Id).ToArray(), DateTime.UtcNow);
        Assert.Empty(TaskDocumentValidator.ValidateRelease(changedInput, adopted));
        Assert.Equal(origin, adopted.Questions[0].Origin);
        Assert.NotEqual(document.Questions[0].Acceptance!.InputFingerprint, adopted.Questions[0].Acceptance!.InputFingerprint);
    }

    [Fact]
    public void Unchanged_material_rewrite_keeps_its_revision_so_questions_stay_current()
    {
        var request = Resolve(Reading());
        var document = TaskAssembly.AcceptMaterials(request, Empty, new([new(MaterialId, null, "שלום עולם")])).Document!;
        document = TaskAssembly.AcceptQuestions(request, document, Questions(Question("text-input"), Question("text-input")));
        var rewritten = TaskAssembly.ReplaceMaterial(new(request, document, MaterialId), new(MaterialId, null, "שלום עולם"));
        Assert.Equal(document.Materials[0].Revision, rewritten.Materials[0].Revision);
        Assert.Empty(TaskDocumentValidator.ValidateRelease(request, rewritten));
    }

    [Fact]
    public void Adoption_and_question_preflight_cannot_waive_strict_material_length()
    {
        var plan = Reading();
        var request = Resolve(plan);
        var document = TaskAssembly.AcceptMaterials(request, Empty, new([new(MaterialId, null, "שלום עולם")])).Document!;
        plan = plan with { Materials = [plan.Materials[0] with { Length = new("range", Lower: 100, Upper: 150) }] };
        var strict = Resolve(plan);
        Assert.Throws<TaskValidationException>(() => TaskAssembly.Adopt(strict, document, [MaterialId], [], DateTime.UtcNow));
        Assert.Throws<TaskValidationException>(() => TaskAssembly.AcceptQuestions(strict, document, Questions(Question("text-input"), Question("text-input"))));
    }

    [Fact]
    public void Identical_requirements_are_deterministic_and_unsafe_drafts_have_bounded_errors()
    {
        var request = Resolve(Numeric());
        Assert.Equal(TaskRequestResolver.Fingerprint(request), TaskRequestResolver.Fingerprint(Resolve(Numeric())));
        var document = new TaskDocument(new string('x', 101), null, [], Enumerable.Range(1, 120).Select(i =>
            new DocumentQuestion(i.ToString("x32"), new string('x', 501), new("numeric-input"), new("bad"), 200, new("manual"), null)).ToArray());
        var check = TaskDocumentValidator.ValidateDraft(request, document);
        Assert.NotEmpty(check.Errors);
        Assert.InRange(check.Errors.Count, 1, 100);
        Assert.InRange(check.Diagnostics.Count, 0, 100);
        Assert.All(check.Errors.Values.SelectMany(v => v), message => Assert.True(message.Length <= 500));
    }

    [Fact]
    public void Selected_adoption_is_strict_even_after_display_diagnostics_are_full()
    {
        var request = Resolve(Numeric(20));
        var document = TaskAssembly.AcceptQuestions(request, Empty,
            Questions(Enumerable.Repeat(Question(), 20).ToArray()));
        document = WithFiveDiagnosticsPerQuestion(document);
        var changedInput = request with { Settings = request.Settings with { Topic = "נושא חדש" } };
        Assert.Contains("remainingErrors", TaskDocumentValidator.ValidateDraft(changedInput, document).Diagnostics.Keys);
        Assert.Throws<TaskValidationException>(() => TaskAssembly.Adopt(changedInput, document, [],
            [document.Questions[^1].Id], DateTime.UtcNow));
    }

    [Fact]
    public void Incomplete_questions_cannot_hide_a_strict_material_failure()
    {
        var plan = Reading() with { Defaults = Reading().Defaults with { QuestionCount = 20 } };
        var request = Resolve(plan);
        var document = TaskAssembly.AcceptMaterials(request, Empty, new([new(MaterialId, null, "שלום עולם")])).Document!;
        document = TaskAssembly.AcceptQuestions(request, document, Questions(Enumerable.Repeat(Question("text-input"), 20).ToArray()));
        document = WithFiveDiagnosticsPerQuestion(document);
        var strict = Resolve(plan with { Materials = [plan.Materials[0] with { Length = new("range", Lower: 120, Upper: 130) }] });
        Assert.Contains("remainingErrors", TaskDocumentValidator.ValidateDraft(strict, document).Diagnostics.Keys);
        Assert.Null(TaskAssembly.AcceptMaterials(strict, document, new([new(MaterialId, null, "קצר מדי")])).Document);
        Assert.Throws<TaskValidationException>(() => TaskAssembly.Adopt(strict, document, [MaterialId], [], DateTime.UtcNow));
    }

    [Fact]
    public void Rejected_candidates_are_detached_evidence_and_null_is_not_a_candidate()
    {
        var candidate = new MaterialCandidateBatch([new(MaterialId, null, "original"), new(OtherId, null, "extra")]);
        var result = TaskAssembly.AcceptMaterials(Resolve(Reading()), Empty, candidate);
        Assert.Null(result.Document);
        candidate.Materials[0] = candidate.Materials[0] with { Body = "mutated" };
        Assert.Equal("original", result.Candidate.Materials[0].Body);
        Assert.Throws<TaskValidationException>(() => TaskAssembly.AcceptMaterials(Resolve(Reading()), Empty, null!));
    }

    [Fact]
    public void Accepted_materials_must_leave_room_for_the_required_questions()
    {
        var plan = Numeric(10) with
        {
            Materials = [Reading().Materials[0] with { Length = null, Controls = [] },
            Reading().Materials[0] with { Id = OtherId, Length = null, Controls = [] }]
        };
        var candidate = new MaterialCandidateBatch([new(MaterialId, null, new string('א', 3990)), new(OtherId, null, new string('ב', 3990))]);
        var request = Resolve(plan);
        Assert.Null(TaskAssembly.AcceptMaterials(request, Empty, candidate).Document);
        var manual = Empty with
        {
            Materials = candidate.Materials.Select(m => new MaterialContent(m.Id, 1, m.Title, m.Body,
            new("manual"), new(TaskRequestResolver.Fingerprint(request), []))).ToArray()
        };
        var check = TaskDocumentValidator.ValidateDraft(request, manual);
        Assert.Empty(check.Errors);
        Assert.NotEmpty(check.Diagnostics);
        Assert.Throws<TaskValidationException>(() => TaskAssembly.Adopt(request, manual, [MaterialId], [], DateTime.UtcNow));
    }
}
