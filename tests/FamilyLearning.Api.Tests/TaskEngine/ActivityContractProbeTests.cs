using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.Tests.Fixtures;
using FamilyLearning.Evaluation;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ActivityContractProbeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Core_protocol_accepts_either_refusal_branch_and_preserves_untargeted_content(bool refusalClarifies)
    {
        AiFixtures.ScriptedChat? chat = null;
        using var provider = chat = new AiFixtures.ScriptedChat
        {
            Respond = text => Reply(chat!.Requests.Count, JsonNode.Parse(text.Split('\n')[^1])!, refusalClarifies)
        };
        using var service = Service(provider);
        var report = new ActivityProbeReport();
        await ActivityContractProbe.RunAsync(service, report, () => Task.CompletedTask, default);
        Assert.Equal(17, provider.Requests.Count);
        Assert.Equal(8, report.Cases.Count);
        Assert.All(report.Cases, result => Assert.True(result.Passed));
        var refusal = report.Cases.Single(result => result.Id == "unsupported-refusal");
        Assert.Contains("מפתח התשובות", refusal.BeforePlan!.Guidance);
        Assert.Equal(JsonSerializer.Serialize(refusal.BeforePlan), JsonSerializer.Serialize(refusal.Plan));
        Assert.Equal(JsonSerializer.Serialize(refusal.BeforeDocument), JsonSerializer.Serialize(refusal.Document));
        Assert.Equal("השאלות יעסקו בכל שלושת הטקסטים.", report.Cases.Single(result => result.Id == "new-text-only").Plan!.Questions.Guidance);
        var writing = JsonNode.Parse(provider.Requests[12].Input.Split('\n')[^1])!;
        var polish = JsonNode.Parse(provider.Requests[13].Input.Split('\n')[^1])!;
        Assert.Single(writing["targetIds"]!.AsArray());
        Assert.Single(polish["materials"]!.AsArray());
        Assert.Equal(2, polish["retained"]!.AsArray().Count);
        Assert.Equal(2, JsonNode.Parse(provider.Requests[16].Input.Split('\n')[^1])!["additionalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task First_invalid_strict_response_stops_the_protocol_and_retains_failure()
    {
        using var provider = new AiFixtures.ScriptedChat("{}");
        using var service = Service(provider);
        var report = new ActivityProbeReport();
        await Assert.ThrowsAnyAsync<Exception>(() => ActivityContractProbe.RunAsync(service, report, () => Task.CompletedTask, default));
        Assert.Single(provider.Requests);
        Assert.False(Assert.Single(report.Cases).Passed);
        Assert.Equal("failed", Assert.Single(report.Steps).Outcome);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("formats")]
    public async Task New_text_still_rejects_unrequested_plan_changes(string field)
    {
        AiFixtures.ScriptedChat? chat = null;
        using var provider = chat = new AiFixtures.ScriptedChat
        {
            Respond = text =>
            {
                var response = JsonNode.Parse(Reply(chat!.Requests.Count, JsonNode.Parse(text.Split('\n')[^1])!, false))!;
                if (chat.Requests.Count == 11)
                {
                    var plan = response["result"]!["change"]!["plan"]!;
                    if (field == "name") plan["name"] = "שם אחר";
                    else plan["questions"]!["formats"] = new JsonArray("numeric-input");
                }
                return response.ToJsonString();
            }
        };
        using var service = Service(provider);
        var report = new ActivityProbeReport();
        var error = await Assert.ThrowsAsync<ActivityProbeCheckException>(() =>
            ActivityContractProbe.RunAsync(service, report, () => Task.CompletedTask, default));
        Assert.Equal("retained-plan", error.Check);
        Assert.Equal(11, provider.Requests.Count);
        Assert.False(report.Cases[^1].Passed);
    }

    private static string Reply(int call, JsonNode input, bool refusalClarifies)
    {
        if (call == 1) return Serialize(new { result = new RevisionDecision(null, "מה תרצו לשנות?", null) });
        if (call is 2 or 7)
        {
            var plan = LearningPlanFixture.Reading() with
            {
                Settings = new("גינה", call == 2 ? "כיתה ג׳" : "כיתה ד׳", call == 2 ? "easy" : "medium", 2),
                Materials = [LearningPlanFixture.Reading().Materials[0] with { Id = null }]
            };
            return Serialize(new { result = new { proposal = plan, clarification = (string?)null }, assumptions = new[] { "הסברים במפתח התשובות אינם נתמכים." } });
        }
        if (call is 3 or 12) return EvaluationFixtures.MaterialIdeas();
        if (call is 4 or 13) return Serialize(new MaterialCandidateBatch(input["targetIds"]!.AsArray()
            .Select(id => new MaterialCandidate(id!.GetValue<string>(), "בגינה", "ילדה וילד שתלו פרחים בגינה. הם השקו אותם בכל בוקר.")).ToArray()));
        if (call is 5 or 14) return new JsonObject { ["materials"] = input["materials"]!.DeepClone() }.ToJsonString();
        if (call is 6 or 15) return Serialize(new QuestionCandidateBatch("הגינה", "ענו", [Question("text-input"), Question("text-input")]));
        if (call is 8 or 9) return Serialize(new
        {
            result = call == 9 && refusalClarifies
            ? new RevisionDecision(null, "הסברים במפתח התשובות אינם נתמכים. תרצו להמשיך בלי ההסברים?", null)
            : new RevisionDecision("תשובה להורה", null, null)
        });
        if (call is 10 or 11 or 16)
        {
            var plan = input["plan"]!.Deserialize<LearningPlan>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            plan = call switch
            {
                10 => plan with { Materials = [plan.Materials[0] with { Label = "טקסט הגינה" }, .. plan.Materials.Skip(1)] },
                11 => plan with
                {
                    Materials = [.. plan.Materials, new(null, "טקסט נוסף", "generated", "סיפור אחר על גינה", null, new("target", 60))],
                    Questions = plan.Questions with { Guidance = "השאלות יעסקו בכל שלושת הטקסטים." }
                },
                _ => plan with { Settings = plan.Settings with { QuestionCount = 4 } }
            };
            var change = ActivityRevisionTests.Change(plan) with { Questions = new(call == 16 ? "append" : "none", null, []) };
            return Serialize(new { result = new RevisionDecision(null, null, change) });
        }
        if (call == 17) return Serialize(new QuestionAdditionBatch([Question() with { Prompt = "כמה הם 2 ועוד 2?", Answer = new("4") }, Question() with { Prompt = "כמה הם 2 ועוד 3?", Answer = new("5") }]));
        throw new InvalidOperationException("Unexpected paid-protocol stage.");
    }
}
