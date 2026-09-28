using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Generators;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class MathGenerationTests
{
    [Theory]
    [InlineData("addition", "easy", 10)]
    [InlineData("addition", "medium", 50)]
    [InlineData("addition", "hard", 100)]
    [InlineData("subtraction", "easy", 10)]
    [InlineData("subtraction", "medium", 50)]
    [InlineData("subtraction", "hard", 100)]
    [InlineData("division", "easy", 5)]
    [InlineData("division", "medium", 10)]
    [InlineData("division", "hard", 12)]
    public void Generates_correct_bounded_arithmetic(string operation, string difficulty, int maximum)
    {
        var definition = Definition() with { Generation = new("deterministic", "math-v1", new(operation)) };
        Assert.Empty(TemplateValidator.Validate(definition));
        var values = new Dictionary<string, JsonElement>
        {
            ["difficulty"] = JsonSerializer.SerializeToElement(difficulty),
            ["questionCount"] = JsonSerializer.SerializeToElement(20)
        };
        var symbol = operation switch { "addition" => "+", "subtraction" => "−", _ => "÷" };
        for (var seed = 0; seed < 20; seed++)
        {
            var content = MathTaskGenerator.Generate(definition, values, seed);
            Assert.Equal(JsonSerializer.Serialize(content), JsonSerializer.Serialize(MathTaskGenerator.Generate(definition, values, seed)));
            Assert.Equal(20, content.Questions.Length);
            foreach (var question in content.Questions)
            {
                var operands = question.Prompt.Split($" {symbol} ").Select(int.Parse).ToArray();
                Assert.Equal(2, operands.Length);
                Assert.InRange(operands[1], 1, maximum);
                Assert.InRange(operands[0], 1, operation == "division" ? maximum * maximum : maximum);
                var answer = int.Parse(question.Answer.Value);
                if (operation == "addition") Assert.Equal(operands[0] + operands[1], answer);
                else if (operation == "subtraction") Assert.Equal(operands[0] - operands[1], answer);
                else
                {
                    Assert.Equal(0, operands[0] % operands[1]);
                    Assert.Equal(operands[0] / operands[1], answer);
                    Assert.InRange(answer, 1, maximum);
                }
                Assert.True(answer >= 0);
            }
        }
    }

    internal static TaskTemplateDefinition Definition() => new(1, "Times tables",
    [
        new("difficulty", "Difficulty", "select", true, JsonSerializer.SerializeToElement("easy"),
            Options: ["easy", "medium", "hard"]),
        new("questionCount", "Questions", "integer", true, JsonSerializer.SerializeToElement(5), Min: 1, Max: 20)
    ], new("deterministic", "math-v1", new("multiplication")));

    [Fact]
    public void Repeats_seeded_content_and_generates_correct_multiplication_answers()
    {
        var definition = Definition();
        var values = ParameterValidator.Validate(definition.InstanceParameters, new Dictionary<string, JsonElement>()).Values;
        var content = MathTaskGenerator.Generate(definition, values, 42);
        Assert.Equal(JsonSerializer.Serialize(content),
            JsonSerializer.Serialize(MathTaskGenerator.Generate(definition, values, 42)));
        Assert.Equal(5, content.Questions.Length);
        Assert.Equal(5, content.Questions.Select(q => q.Id).Distinct().Count());
        foreach (var question in content.Questions)
        {
            var operands = question.Prompt.Split(" × ").Select(int.Parse).ToArray();
            Assert.All(operands, operand => Assert.InRange(operand, 1, 5));
            Assert.Equal((operands[0] * operands[1]).ToString(), question.Answer.Value);
            Assert.Equal("numeric-input", question.Interaction.Type);
        }
    }

    [Fact]
    public void Rejects_unknown_generator_and_incompatible_parameter_contract()
    {
        var definition = Definition();
        Assert.Empty(TemplateValidator.Validate(definition));
        Assert.NotEmpty(TemplateValidator.Validate(definition with
        {
            Generation = definition.Generation with { Generator = "unknown" }
        }));
        Assert.NotEmpty(TemplateValidator.Validate(definition with { InstanceParameters = [] }));
        Assert.NotEmpty(TemplateValidator.Validate(definition with { SchemaVersion = 99 }));
        Assert.NotEmpty(TemplateValidator.Validate(definition with
        {
            InstanceParameters = [.. definition.InstanceParameters, definition.InstanceParameters[0]]
        }));
    }
}
