using System.Text.Json;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using FamilyLearning.Api.Tests.Fixtures;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ActivityRevisionTests
{
    [Fact]
    public async Task Required_sources_are_never_truncated_to_fit_and_optional_history_is_dropped_first()
    {
        var plan = Supplied() with { Materials = [Supplied().Materials[0] with { Text = new string('א', 3000) }] };
        var input = new ActivityRevisionInput(plan, TaskAssembly.CreateDocument(Resolve(plan)), "מה כתוב?",
            Context: [new("parent", new string('ב', 4000)), new("assistant", new string('ג', 4000)), new("parent", new string('ד', 4000))]);
        var response = Serialize(new { result = new RevisionDecision("תשובה", null, null) });
        using var chat = new AiFixtures.ScriptedChat(response);
        using var service = Service(chat, new() { MaxRequestBytes = 32_000 });
        await service.ReviseAsync(input, default);
        var payload = JsonDocument.Parse(Assert.Single(chat.Requests).Input.Split('\n')[^1]).RootElement;
        Assert.Equal(plan.Materials[0].Text, payload.GetProperty("plan").GetProperty("materials")[0].GetProperty("text").GetString());
        Assert.True(payload.GetProperty("context").GetArrayLength() < 3);
        using var blocked = new AiFixtures.ScriptedChat(response);
        using var small = Service(blocked, new() { MaxRequestBytes = 1000 });
        await Assert.ThrowsAsync<AiGenerationException>(() => small.ReviseAsync(input, default));
        Assert.Empty(blocked.Requests);
    }

    // Frozen refusal regression: fixture responses verify the contract, not live-model judgment.
    [Theory]
    [InlineData("")]
    [InlineData("יש להוסיף הסבר לכל תשובה במפתח התשובות")]
    public async Task Refusal_preserves_even_old_unsupported_requirements_and_makes_only_the_planner_call(string oldGuidance)
    {
        var plan = Numeric() with { Guidance = oldGuidance };
        var current = TaskAssembly.AcceptQuestions(Resolve(plan), TaskAssembly.CreateDocument(Resolve(plan)), Questions());
        var before = Serialize(new { plan, current });
        using var chat = new AiFixtures.ScriptedChat(Serialize(new { result = new { answer = "הוספת הסברים למפתח התשובות אינה נתמכת.", clarification = (string?)null, change = (object?)null } }));
        using var service = Service(chat);
        var reply = await service.ReviseAsync(new(plan, current, "הוסף הסברים למפתח התשובות"), default);
        Assert.Null(reply.Value.Change);
        Assert.NotNull(reply.Value.Answer);
        Assert.Equal(before, Serialize(new { plan, current }));
        var request = Assert.Single(chat.Requests).Input;
        Assert.DoesNotContain("acceptance", request);
        Assert.DoesNotContain("origin", request);
        Assert.DoesNotContain("engineRevision", request);
    }

    [Fact]
    public async Task Reply_cannot_carry_edits_and_invalid_output_is_not_retried()
    {
        var plan = Numeric();
        using var chat = new AiFixtures.ScriptedChat(Serialize(new { result = new { answer = "בוצע", clarification = (string?)null, change = Change(plan) } }));
        using var service = Service(chat);
        await Assert.ThrowsAsync<AiGenerationException>(() => service.ReviseAsync(new(plan, TaskAssembly.CreateDocument(Resolve(plan)), "שנה"), default));
        Assert.Single(chat.Requests);
    }

    [Fact]
    public void Source_addition_requires_exact_confirmed_text_and_retained_sources_cannot_be_rewritten()
    {
        var plan = Supplied();
        var input = new ActivityRevisionInput(plan, TaskAssembly.CreateDocument(Resolve(plan)), "הוסף מקור", Sources: [new("חדש", Source)]);
        var proposed = plan with { Materials = [.. plan.Materials, new(null, "חדש", "supplied", "", Source, null)] };
        var result = ActivityRevisionValidator.Validate(new(null, null, Change(proposed)), input);
        Assert.NotNull(result.Change!.Plan.Materials[1].Id);
        Assert.Equal(Source, result.Change.Plan.Materials[1].Text);
        Assert.Throws<TaskValidationException>(() => ActivityRevisionValidator.Validate(new(null, null, Change(proposed)), input with { Sources = [] }));
        proposed = plan with { Materials = [plan.Materials[0] with { Text = "edited" }] };
        Assert.Throws<TaskValidationException>(() => ActivityRevisionValidator.Validate(new(null, null, Change(proposed)), input));
    }

    [Fact]
    public void Retained_generated_identity_cannot_bypass_confirmed_source_addition()
    {
        var plan = Reading();
        var input = new ActivityRevisionInput(plan, TaskAssembly.CreateDocument(Resolve(plan)), "הוסף מקור");
        var proposed = plan with { Materials = [plan.Materials[0] with { Source = "supplied", Text = "unconfirmed", Length = null }] };
        Assert.Throws<TaskValidationException>(() => ActivityRevisionValidator.Validate(new(null, null, Change(proposed)), input));
    }

    [Fact]
    public void Question_edit_contract_rejects_foreign_duplicate_and_incompatible_targets()
    {
        var plan = Numeric();
        var document = TaskAssembly.AcceptQuestions(Resolve(plan), TaskAssembly.CreateDocument(Resolve(plan)), Questions());
        var input = new ActivityRevisionInput(plan, document, "שנה");
        var id = document.Questions[0].Id;
        var invalid = new[]
        {
            Change(plan) with { Questions = new("selected", null, [new(OtherId, "שנה")]) },
            Change(plan) with { Questions = new("selected", null, [new(id, "א"), new(id, "ב")]) },
            Change(plan) with { Questions = new("none", "שנה", []) },
            Change(plan) with { Questions = new("append", null, []) },
            Change(plan) with { QuestionOrder = [id, id] },
            Change(plan) with { Questions = new("selected", null, [new(id, new string('x', 501))]) }
        };
        foreach (var change in invalid)
            Assert.Throws<TaskValidationException>(() => ActivityRevisionValidator.Validate(new(null, null, change), input));
    }

    [Fact]
    public void Before_creation_edits_must_be_expressed_only_in_requirements()
    {
        var plan = Reading();
        var input = new ActivityRevisionInput(plan, TaskAssembly.CreateDocument(Resolve(plan)), "שנה את הטקסט");
        Assert.Throws<TaskValidationException>(() => ActivityRevisionValidator.Validate(new(null, null,
            Change(plan) with { MaterialEdits = [new(MaterialId, "שנה")] }), input));
        Assert.NotNull(ActivityRevisionValidator.Validate(new(null, null, Change(plan with { Guidance = "new" })), input).Change);
    }

    internal static RevisionChange Change(LearningPlan plan) => new(plan, [], [], new("none", null, []), null);
}
