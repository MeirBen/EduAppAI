using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class QuestionCandidateValidationTests
{
    [Theory]
    [InlineData("missing-points")]
    [InlineData("null-question")]
    [InlineData("provider-owned-id")]
    [InlineData("unsupported-interaction")]
    [InlineData("missing-choice-answer")]
    [InlineData("duplicate-choice")]
    [InlineData("multiline-choice")]
    [InlineData("padded-choice")]
    [InlineData("null-choice")]
    [InlineData("numeric-exponent")]
    [InlineData("numeric-overflow")]
    [InlineData("negative-points")]
    [InlineData("fractional-points")]
    [InlineData("null-content")]
    public void Rejects_invalid_generated_content(string scenario)
    {
        var content = JsonNode.Parse("""
            {"title":"תרגול","instructions":"ענו","questions":[
              {"prompt":"כמה?","interaction":{"type":"numeric-input","options":null},"answer":{"value":"3"},"points":1},
              {"prompt":"מה?","interaction":{"type":"text-input","options":null},"answer":{"value":"ספר"},"points":1},
              {"prompt":"איזה?","interaction":{"type":"single-choice","options":["ספרים","פרחים","עצים"]},"answer":{"value":"ספרים"},"points":1}]}
            """)!;
        content["questions"]![2]!["interaction"] = new JsonObject { ["type"] = "single-choice", ["options"] = new JsonArray("ספרים", "פרחים", "עצים") };
        content["questions"]![2]!["answer"]!["value"] = "ספרים";
        content["questions"]![0]!["interaction"]!["type"] = "numeric-input";
        content["questions"]![0]!["answer"]!["value"] = "3";
        var questions = content["questions"]!.AsArray();
        switch (scenario)
        {
            case "missing-points": questions[0]!.AsObject().Remove("points"); break;
            case "null-question": questions[0] = null; break;
            case "provider-owned-id": questions[1]!["id"] = "q1"; break;
            case "unsupported-interaction": questions[0]!["interaction"]!["type"] = "html"; break;
            case "missing-choice-answer": questions[2]!["answer"]!["value"] = "missing"; break;
            case "duplicate-choice": questions[2]!["interaction"]!["options"]![1] = "ספרים"; break;
            case "multiline-choice": questions[2]!["interaction"]!["options"]![1] = "פרחים\nעצים"; break;
            case "padded-choice": questions[2]!["interaction"]!["options"]![1] = " פרחים "; break;
            case "null-choice": questions[2]!["interaction"]!["options"]![0] = null; break;
            case "numeric-exponent": questions[0]!["answer"]!["value"] = "1e3"; break;
            case "numeric-overflow": questions[0]!["answer"]!["value"] = new string('9', 100); break;
            case "negative-points": questions[0]!["points"] = -1; break;
            case "fractional-points": questions[0]!["points"] = 1.5; break;
            case "null-content": content = null!; break;
        }
        try
        {
            var value = content?.Deserialize<QuestionCandidateBatch>(EngineJson.Options);
            var request = LearningPlanFixture.Resolve(LearningPlanFixture.Mixed());
            Assert.NotEmpty(TaskDocumentValidator.ValidateQuestionBatch(request, TaskAssembly.CreateDocument(request), value!));
        }
        catch (JsonException) { /* Missing required members or fractional points are rejected while parsing. */ }
    }
}
