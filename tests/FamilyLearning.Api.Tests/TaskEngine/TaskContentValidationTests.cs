using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using FamilyLearning.Api.Tests.Fixtures;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class TaskContentValidationTests
{
    [Fact]
    public void A_repeated_task_title_alone_does_not_satisfy_a_minimum_word_count()
    {
        var content = new TaskContent("כותרת בלבד", "", [new("text", "כותרת בלבד")], []);
        Assert.Equal(0, TaskContentValidator.CountContentWords(content));
        Assert.Contains("contentBlocks.wordCount", TaskContentValidator.Validate(content, contentWordCount: new(1)).Keys);
    }

    [Fact]
    public void Word_limits_count_whitespace_and_vowel_points_across_text_blocks_only()
    {
        var content = new TaskContent("כותרת שלא נספרת", "הוראות שלא נספרות",
            [new("text", "כותרת שלא נספרת\r\n\nשָׁלוֹם־עוֹלָם\tמילה\u00a0נוספת"), new("text", "שתי מילים")], []);
        Assert.Equal(5, TaskContentValidator.CountContentWords(content));
        Assert.DoesNotContain("contentBlocks.wordCount", TaskContentValidator.Validate(content, contentWordCount: new(5, 5)).Keys);
        Assert.Contains("contentBlocks.wordCount", TaskContentValidator.Validate(content, contentWordCount: new(6, 6)).Keys);
    }

    [Theory]
    [InlineData("missing-points")]
    [InlineData("null-question")]
    [InlineData("duplicate-id")]
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
    [InlineData("unknown-block")]
    [InlineData("too-many-questions")]
    [InlineData("oversize-total")]
    public void Rejects_invalid_generated_content(string scenario)
    {
        var content = AiFixtures.Content(count: 3);
        content["questions"]![2]!["interaction"] = new JsonObject { ["type"] = "single-choice", ["options"] = new JsonArray("ספרים", "פרחים") };
        content["questions"]![2]!["answer"]!["value"] = "ספרים";
        content["questions"]![0]!["interaction"]!["type"] = "numeric-input";
        content["questions"]![0]!["answer"]!["value"] = "3";
        var questions = content["questions"]!.AsArray();
        switch (scenario)
        {
            case "missing-points": questions[0]!.AsObject().Remove("points"); break;
            case "null-question": questions[0] = null; break;
            case "duplicate-id": questions[1]!["id"] = "q1"; break;
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
            case "unknown-block": content["contentBlocks"]![0]!["type"] = "html"; break;
            case "too-many-questions":
                while (questions.Count < 21)
                {
                    var question = questions[0]!.DeepClone();
                    question["id"] = $"q{questions.Count}";
                    questions.Add(question);
                }
                break;
            case "oversize-total": content["contentBlocks"] = new JsonArray(Enumerable.Range(0, 3).Select(_ => (JsonNode)new JsonObject { ["type"] = "text", ["text"] = new string('a', 3000) }).ToArray()); break;
        }
        try
        {
            var value = content?.Deserialize<TaskContent>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotEmpty(TaskContentValidator.Validate(value));
        }
        catch (JsonException) { /* Missing required members or fractional points are rejected while parsing. */ }
    }
}
