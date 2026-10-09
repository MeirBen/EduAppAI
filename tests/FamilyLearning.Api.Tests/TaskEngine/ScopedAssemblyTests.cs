using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using static FamilyLearning.Api.Tests.TaskEngine.ActivityRevisionTests;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ScopedAssemblyTests
{
    [Fact]
    public void One_addition_to_a_mixed_batch_preserves_originals_and_rejects_duplicates()
    {
        var plan = Mixed();
        var current = TaskAssembly.AcceptQuestions(Resolve(plan), TaskAssembly.CreateDocument(Resolve(plan)),
            new("שם", "הנחיות", [Question(), Question("text-input"), Question("single-choice")]));
        var proposed = plan with { Settings = plan.Settings! with { QuestionCount = 4 } };
        var scope = RevisionScope.Derive(plan, current, Change(proposed));
        var working = RevisionScope.PrepareDocument(plan, current, proposed, scope);
        var input = new QuestionAdditionInput(Resolve(proposed), working, 1, "שאלה על מפת האוצר");
        var addition = new QuestionAdditionBatch([Question() with { Prompt = "מה המספר במפה?" }]);
        var output = TaskAssembly.AppendQuestions(input, addition);
        Assert.Equal(working.Title, output.Title);
        Assert.Equal(working.Instructions, output.Instructions);
        Assert.Equal(Serialize(working.Questions), Serialize(output.Questions[..3]));
        Assert.Equal(4, output.Questions.Length);
        Assert.Empty(TaskDocumentValidator.ValidateRelease(Resolve(proposed), output));
        Assert.Throws<TaskValidationException>(() => TaskAssembly.AppendQuestions(input, new([Question()])));
        Assert.Throws<TaskValidationException>(() => TaskAssembly.AppendQuestions(input, new([.. addition.Questions, .. addition.Questions])));
    }

    [Fact]
    public void Explicit_new_material_batch_and_polish_leave_retained_text_unchanged()
    {
        var plan = Reading();
        var current = TaskAssembly.AcceptMaterials(Resolve(plan), TaskAssembly.CreateDocument(Resolve(plan)), Materials()).Document!;
        current = TaskAssembly.AcceptQuestions(Resolve(plan), current, Questions("text-input"));
        var proposed = plan with { Materials = [.. plan.Materials, plan.Materials[0] with { Id = OtherId }] };
        var scope = RevisionScope.Derive(plan, current, Change(proposed));
        var request = Resolve(proposed);
        var working = RevisionScope.PrepareDocument(plan, current, proposed, scope);
        var output = TaskAssembly.AcceptMaterials(request, working, new([new(OtherId, "חדש", "טקסט חדש")]), targets: [OtherId]).Document!;
        Assert.Equal(Serialize(working.Materials[0]), Serialize(output.Materials[0]));
        output = TaskAssembly.PolishMaterials(new(request, output, [OtherId]), new([new(OtherId, "חדש", "טקסט חדש משופר")]));
        Assert.Equal(Serialize(working.Materials[0]), Serialize(output.Materials[0]));
        Assert.Equal("טקסט חדש משופר", output.Materials[1].Body);
    }
}
