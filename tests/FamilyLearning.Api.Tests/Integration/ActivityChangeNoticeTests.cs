using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ActivityChangeNoticeTests
{
    [Fact]
    public void A_summary_is_stored_once_and_reaches_the_model_as_its_joined_statements()
    {
        ActivityChatTurn[] chat =
        [
            new("parent", "בקשה", DateTime.UtcNow),
            new("assistant", "", DateTime.UtcNow, Outcome: "completed", Changes: ["שאלות שנוספו: 2.", "כותרת התוכן עודכנה."]),
        ];
        var context = ActivityChat.Context(chat);
        Assert.Equal("שאלות שנוספו: 2. כותרת התוכן עודכנה.", context[1].Text);
    }

    [Fact]
    public void Removal_and_reorder_notices_follow_actual_surviving_content()
    {
        var before = Numeric(3);
        var previous = TaskAssembly.AcceptQuestions(Resolve(before), TaskAssembly.CreateDocument(Resolve(before)),
            new("כותרת", "הוראות", [Question(), Question(), Question()]));
        var after = before with { Settings = before.Settings with { QuestionCount = 2 } };
        var current = previous with { Questions = [previous.Questions[2], previous.Questions[0]] };
        var notice = string.Join(" ", ActivityChangeNotice.Describe(before, previous, after, current));
        Assert.Contains("שאלות שהוסרו: 1", notice);
        Assert.Contains("סדר השאלות שונה", notice);
        Assert.DoesNotContain("שאלות שעודכנו", notice);
        Assert.DoesNotContain("נוצרו מחדש", notice);
    }

    [Fact]
    public void Acceptance_and_origin_updates_do_not_claim_question_text_changed()
    {
        var plan = Numeric();
        var previous = TaskAssembly.AcceptQuestions(Resolve(plan), TaskAssembly.CreateDocument(Resolve(plan)), Questions());
        var current = previous with
        {
            Questions = previous.Questions.Select(q => q with
            {
                Origin = new("generated", int.MaxValue),
                Acceptance = q.Acceptance! with { AdoptedAtUtc = DateTime.UtcNow }
            }).ToArray()
        };
        var notice = string.Join(" ", ActivityChangeNotice.Describe(plan, previous, plan, current));
        Assert.Contains("נשארו ללא שינוי", notice);
        Assert.DoesNotContain("שאלות שעודכנו", notice);
        Assert.DoesNotContain("נוצרו מחדש", notice);
    }

    [Fact]
    public void Precreation_plan_edits_do_not_claim_missing_text_has_been_written()
    {
        var before = Numeric();
        var previous = TaskAssembly.CreateDocument(Resolve(before));
        var after = before with { Materials = Reading().Materials };
        var current = TaskAssembly.AlignSources(Resolve(after), previous);
        var notice = string.Join(" ", ActivityChangeNotice.Describe(before, previous, after, current));
        Assert.Contains("דרישות לטקסטים שנוספו: 1", notice);
        Assert.DoesNotContain("טקסטים שעודכנו", notice);
        Assert.DoesNotContain("שאלות", notice);
    }

    [Fact]
    public void Combined_notices_remain_bounded_without_exposing_content_or_answer_keys()
    {
        var before = Reading() with
        {
            Materials = [Reading().Materials[0], Reading().Materials[0] with { Id = OtherId }]
        };
        var previous = TaskAssembly.AcceptMaterials(Resolve(before), TaskAssembly.CreateDocument(Resolve(before)),
            new([new(MaterialId, "כותרת", "טקסט קודם"), new(OtherId, "כותרת", "טקסט נוסף")])).Document!;
        previous = TaskAssembly.AcceptQuestions(Resolve(before), previous, Questions("text-input"));
        var after = before with
        {
            Name = new string('א', EngineValidation.NameLength),
            Guidance = "הנחיות מעודכנות",
            Questions = before.Questions with { Guidance = "שאלות מעודכנות" },
            Materials = [before.Materials[1] with { Label = new string('ב', EngineValidation.NameLength), Guidance = "עדכון" }, before.Materials[0]]
        };
        var current = previous with
        {
            Title = "כותרת חדשה",
            Instructions = "הוראות חדשות",
            Materials = [previous.Materials[1] with { Body = "private-material-body" }, previous.Materials[0]],
            Questions = [previous.Questions[1] with { Prompt = "private-question-prompt", Answer = new("private-key") }]
        };
        var notice = string.Join(" ", ActivityChangeNotice.Describe(before, previous, after, current));
        Assert.Contains(after.Name, notice);
        Assert.Contains(after.Materials[0].Label, notice);
        Assert.InRange(notice.Length, 1, EngineValidation.RevisionReplyLength);
        Assert.DoesNotContain("private-", notice);
    }
}
