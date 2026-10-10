using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using static FamilyLearning.Api.Tests.TaskEngine.ActivityRevisionTests;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class RevisionScopeTests
{
    [Fact]
    public void Create_allows_missing_text_with_a_strict_per_material_range()
    {
        var plan = Reading() with { Materials = [Reading().Materials[0] with { Length = new("range", Lower: 2, Upper: 10) }] };
        Assert.Equal([MaterialId], RevisionScope.ForCreate(plan, TaskAssembly.CreateDocument(Resolve(plan))).NewMaterials);
    }

    [Fact]
    public void Shared_changes_rebuild_questions_even_without_texts_and_guidance_is_never_cosmetic()
    {
        var plan = Numeric();
        var current = Complete(plan);
        var shared = RevisionScope.Derive(plan, current, Change(plan with { Guidance = "new" }));
        Assert.Equal("all", shared.Questions);
        Assert.Empty(shared.Rewrites);
        Assert.Equal("all", RevisionScope.Derive(plan, current, Change(plan with { Questions = plan.Questions with { Guidance = " " } })).Questions);
    }

    [Fact]
    public void Title_and_instruction_guidance_regenerates_nothing_and_keeps_content_current()
    {
        var plan = Numeric();
        var current = Complete(plan);
        var after = plan with { DocumentGuidance = "כותרת והוראות ללא ניקוד" };
        var scope = RevisionScope.Derive(plan, current, Change(after));
        Assert.Equal("none", scope.Questions);
        Assert.False(scope.RequiresComplete);
        Assert.Equal(TaskRequestResolver.Fingerprint(Resolve(plan)), TaskRequestResolver.Fingerprint(Resolve(after)));
        Assert.Empty(TaskDocumentValidator.ValidateRelease(Resolve(after), RevisionScope.PrepareDocument(plan, current, after, scope)));
    }

    [Fact]
    public void Metadata_and_precreation_changes_do_not_generate_or_clear_existing_diagnostics()
    {
        var plan = Reading();
        var current = TaskAssembly.CreateDocument(Resolve(plan));
        var scope = RevisionScope.Derive(plan, current, Change(plan with { Settings = plan.Settings! with { Audience = "כיתה ד" } }));
        Assert.False(scope.RequiresComplete);
        Assert.Empty(scope.NewMaterials);
        Assert.Equal("none", scope.Questions);
        Assert.Equal(Serialize(current), Serialize(RevisionScope.PrepareDocument(plan, current, plan with { Name = "חדש" }, scope)));
    }

    [Fact]
    public void Append_and_removal_preserve_current_survivors_but_stale_questions_require_a_choice()
    {
        var plan = Numeric();
        var current = Complete(plan);
        var bigger = plan with { Settings = plan.Settings! with { QuestionCount = 3 } };
        Assert.Equal("append", RevisionScope.Derive(plan, current, Change(bigger)).Questions);
        var smaller = plan with { Settings = plan.Settings! with { QuestionCount = 1 } };
        Assert.NotNull(RevisionScope.Derive(plan, current, Change(smaller)).Clarification);
        var remove = Change(smaller) with { QuestionOrder = [current.Questions[1].Id] };
        var scope = RevisionScope.Derive(plan, current, remove);
        Assert.Null(scope.Clarification);
        Assert.Equal("preserve", scope.Questions);
        var output = RevisionScope.PrepareDocument(plan, current, smaller, scope);
        Assert.Equal(current.Questions[1].Id, Assert.Single(output.Questions).Id);
        Assert.Equal(current.Questions[1].Prompt, output.Questions[0].Prompt);
        Assert.Equal(current.Questions[1].Origin, output.Questions[0].Origin);
        Assert.Empty(TaskDocumentValidator.ValidateRelease(Resolve(smaller), output));
        current.Questions[0] = current.Questions[0] with { Acceptance = null };
        Assert.NotNull(RevisionScope.Derive(plan, current, Change(bigger)).Clarification);
    }

    [Fact]
    public void Removing_last_required_format_clarifies_without_regeneration()
    {
        var plan = Numeric(3) with { Questions = new(["numeric-input", "text-input"], null, "") };
        var current = TaskAssembly.AcceptQuestions(Resolve(plan), TaskAssembly.CreateDocument(Resolve(plan)),
            new("כותרת", null, [Question(), Question(), Question("text-input")]));
        var proposed = plan with { Settings = plan.Settings! with { QuestionCount = 2 } };
        Assert.NotNull(RevisionScope.Derive(plan, current, Change(proposed) with { QuestionOrder = [current.Questions[0].Id, current.Questions[1].Id] }).Clarification);
    }

    [Fact]
    public void Strict_total_defers_multiple_rewrites_or_a_new_text_beside_retained_texts()
    {
        var plan = Reading() with { Materials = [Reading().Materials[0] with { Length = null }, Reading().Materials[0] with { Id = OtherId, Length = null }], TotalLength = new("range", Lower: 3, Upper: 10) };
        var request = Resolve(plan);
        var current = TaskAssembly.AcceptMaterials(request, TaskAssembly.CreateDocument(request), new([new(MaterialId, null, "a b"), new(OtherId, null, "c d")])).Document!;
        current = TaskAssembly.AcceptQuestions(request, current, Questions("text-input"));
        Assert.NotNull(RevisionScope.Derive(plan, current, Change(plan with { Guidance = "new" })).Clarification);
        Assert.Null(RevisionScope.Derive(plan, current, Change(plan) with { MaterialEdits = [new(MaterialId, "שכתב")] }).Clarification);
        var added = plan with { Materials = [.. plan.Materials, plan.Materials[0] with { Id = new string('4', 32) }] };
        Assert.NotNull(RevisionScope.Derive(plan, current, Change(added)).Clarification);
    }

    [Fact]
    public void Create_preserves_current_texts_and_rejects_implicit_stale_repair()
    {
        var plan = Reading();
        var request = Resolve(plan);
        var current = TaskAssembly.AcceptMaterials(request, TaskAssembly.CreateDocument(request), Materials()).Document!;
        Assert.Empty(RevisionScope.ForCreate(plan, current).NewMaterials);
        current.Materials[0] = current.Materials[0] with { Acceptance = null };
        Assert.Throws<TaskValidationException>(() => RevisionScope.ForCreate(plan, current));
    }

    [Fact]
    public void Format_order_is_not_an_effective_change_and_create_rejects_complete_content()
    {
        var plan = Numeric() with { Questions = new(["text-input", "numeric-input"], null, "") };
        var request = Resolve(plan);
        var current = TaskAssembly.AcceptQuestions(request, TaskAssembly.CreateDocument(request), new("כותרת", null, [Question("text-input"), Question()]));
        var reordered = plan with { Questions = plan.Questions with { Formats = ["numeric-input", "text-input"] } };
        var work = RevisionScope.Derive(plan, current, Change(reordered));
        Assert.False(work.RequiresComplete);
        Assert.Empty(TaskDocumentValidator.ValidateRelease(Resolve(reordered), RevisionScope.PrepareDocument(plan, current, reordered, work)));
        Assert.Throws<TaskValidationException>(() => RevisionScope.ForCreate(plan, current));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Material_labels_preserve_content_and_only_rebase_previously_current_items(bool staleQuestion)
    {
        var plan = Reading();
        var input = Resolve(plan);
        var current = TaskAssembly.AcceptMaterials(input, TaskAssembly.CreateDocument(input), Materials()).Document!;
        current = TaskAssembly.AcceptQuestions(input, current, Questions("text-input"));
        if (staleQuestion) current.Questions[0] = current.Questions[0] with { Acceptance = null };
        var after = plan with { Materials = [plan.Materials[0] with { Label = "תווית חדשה" }] };
        var work = RevisionScope.Derive(plan, current, Change(after));
        Assert.Empty(work.Rewrites);
        Assert.Equal("none", work.Questions);
        Assert.False(work.RequiresComplete);
        Assert.Null(work.Clarification);
        var prepared = RevisionScope.PrepareDocument(plan, current, after, work);
        Assert.Equal(Serialize(current.Materials[0] with { Acceptance = prepared.Materials[0].Acceptance }), Serialize(prepared.Materials[0]));
        for (var i = 0; i < current.Questions.Length; i++)
            Assert.Equal(Serialize(current.Questions[i] with { Acceptance = prepared.Questions[i].Acceptance }), Serialize(prepared.Questions[i]));
        var errors = TaskDocumentValidator.ValidateRelease(Resolve(after), prepared);
        if (staleQuestion)
        {
            Assert.Null(prepared.Questions[0].Acceptance);
            Assert.Equal("questions[0].stale", Assert.Single(errors).Key);
        }
        else Assert.Empty(errors);
    }

    [Fact]
    public void Material_label_change_can_be_combined_with_appending_questions()
    {
        var plan = Supplied();
        var current = Complete(plan);
        var after = plan with
        {
            Materials = [plan.Materials[0] with { Label = "שם מקור חדש" }],
            Settings = plan.Settings with { QuestionCount = 3 }
        };
        var change = Change(after) with { Questions = new("append", "על המקור", []) };
        Assert.NotNull(ActivityRevisionValidator.Validate(new(null, null, change), new(plan, current, "שנה את שם המקור והוסף שאלה")).Change);
        var work = RevisionScope.Derive(plan, current, change);
        Assert.Equal("append", work.Questions);
        Assert.Empty(work.Rewrites);
        Assert.Null(work.Clarification);
    }

    private static TaskDocument Complete(LearningPlan plan) => TaskAssembly.AcceptQuestions(Resolve(plan), TaskAssembly.CreateDocument(Resolve(plan)), Questions());
}
