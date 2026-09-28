using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Generators;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class MathGenerationTests
{
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
