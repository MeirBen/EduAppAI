using System.Net;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.Tests.Fixtures;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class AiPromptContractTests
{
    // These assertions cover the provider contract; model compliance needs live evaluation.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Planning_calls_share_language_difficulty_and_length_rules(bool revision)
    {
        var prompt = await PlanningPrompt(revision);
        Assert.Contains("Record niqqud and other language presentation in the plan only when the parent explicitly asks for it.", prompt);
        Assert.Contains("difficulty is easy\nthrough third grade and medium above.", prompt);
        Assert.Contains("Word counts are approximate targets, even when phrased as exact; note that in assumptions.", prompt);
        Assert.Contains("states both a minimum and a larger maximum.", prompt);
        Assert.Contains("Preserve untouched fields and exact sources.", prompt);
        Assert.DoesNotContain("Content stages obey", prompt);
    }

    [Fact]
    public async Task Authoring_discloses_omitted_key_extras_while_revision_preserves_the_refusal_contract()
    {
        var authoring = await PlanningPrompt(revision: false);
        Assert.Contains("parent is unsupported; when one is requested, leave it out and say so in assumptions.", authoring);
        Assert.DoesNotContain("explain the limitation in a clarification and make no change", authoring);
        var revision = await PlanningPrompt(revision: true);
        Assert.Contains("Unsupported requests must be refused without changing ANY field", revision);
        Assert.Contains("including old unsupported requirements", revision);
        Assert.DoesNotContain("leave it out and say so in assumptions", revision);
    }

    [Fact]
    public async Task Revision_copies_only_supplied_texts_and_changes_generated_texts_in_place()
    {
        var revision = await PlanningPrompt(revision: true);
        Assert.Contains("never rewrite targets; to transform\none, add a separate generated material.", revision);
        Assert.Contains("A requested change to a generated text, such as a new genre or length, changes that\nmaterial in place.", revision);
        Assert.DoesNotContain("For a transformation, add a separate generated material.", revision);
    }

    [Theory]
    [InlineData("ideas")]
    [InlineData("materials")]
    [InlineData("polish")]
    [InlineData("replace-material")]
    [InlineData("questions")]
    [InlineData("append-questions")]
    [InlineData("replace-question")]
    public async Task Content_calls_obey_resolved_requirements_without_planning_permissions(string stage)
    {
        var request = Resolve(Reading());
        var empty = TaskAssembly.CreateDocument(request);
        var current = TaskAssembly.AcceptMaterials(request, empty, Materials()).Document!;
        current = TaskAssembly.AcceptQuestions(request, current, Questions("text-input"));
        using var chat = new AiFixtures.ScriptedChat { FailureStatus = HttpStatusCode.ServiceUnavailable };
        var service = Service(chat);
        Task Call() => stage switch
        {
            "ideas" => service.GenerateMaterialIdeasAsync(TaskAssembly.PrepareMaterials(request, empty)!, [], default),
            "materials" => service.GenerateMaterialsAsync(TaskAssembly.PrepareMaterials(request, empty)!, new("רעיון", "מבנה"), default),
            "polish" => service.PolishMaterialsAsync(new(request, current), default),
            "replace-material" => service.ReplaceMaterialAsync(new(request, current, MaterialId), default),
            "questions" => service.GenerateQuestionsAsync(TaskAssembly.PrepareQuestions(request, current), [], default),
            "append-questions" => service.AppendQuestionsAsync(new(request, current with { Questions = current.Questions[..1] }, 1), [], default),
            "replace-question" => service.ReplaceQuestionAsync(new(request, current, current.Questions[0].Id), default),
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };
        await Assert.ThrowsAsync<AiGenerationException>(Call);
        var prompt = Assert.Single(chat.Requests).Input;
        Assert.Contains("Content stages obey the resulting concrete typed requirements; one-off instructions cannot override them.", prompt);
        Assert.DoesNotContain("Planning may update old requirements", prompt);
        Assert.DoesNotContain("adjustable settings or future parameters", prompt);
        Assert.DoesNotContain("Record niqqud and other language presentation in the plan", prompt);
        Assert.DoesNotContain("through third grade and medium above", prompt);
        Assert.DoesNotContain("even when phrased as exact; note that in assumptions", prompt);
    }

    private static async Task<string> PlanningPrompt(bool revision)
    {
        var plan = Numeric();
        using var chat = new AiFixtures.ScriptedChat { FailureStatus = HttpStatusCode.ServiceUnavailable };
        var service = Service(chat);
        await Assert.ThrowsAsync<AiGenerationException>(() => revision
            ? (Task)service.ReviseAsync(new(plan, TaskAssembly.CreateDocument(Resolve(plan)), "הוסף הסברים למפתח התשובות"), default)
            : service.AuthorAsync(new("פעילות עם הסברים למפתח התשובות"), default));
        return Assert.Single(chat.Requests).Input;
    }
}
