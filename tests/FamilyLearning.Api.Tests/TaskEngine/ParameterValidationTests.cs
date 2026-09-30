using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ParameterValidationTests
{
    private static readonly ParameterDefinition[] Schema =
    [
        new("theme", "Theme", "text", Required: true, MaxLength: 10),
        new("count", "Count", "integer", Default: JsonSerializer.SerializeToElement(5), Min: 1, Max: 20),
        new("level", "Level", "select", Options: ["easy", "hard"]),
        new("retry", "Retry", "boolean")
    ];

    [Theory]
    [InlineData("{}", "theme")]
    [InlineData("{\"theme\":\"   \"}", "theme")]
    [InlineData("{\"theme\":\"12345678901\"}", "theme")]
    [InlineData("{\"theme\":null}", "theme")]
    [InlineData("{\"theme\":\"Space\",\"count\":null}", "count")]
    [InlineData("{\"theme\":\"Space\",\"count\":1.5}", "count")]
    [InlineData("{\"theme\":\"Space\",\"count\":21}", "count")]
    [InlineData("{\"theme\":\"Space\",\"count\":\"5\"}", "count")]
    [InlineData("{\"theme\":\"Space\",\"level\":\"medium\"}", "level")]
    [InlineData("{\"theme\":\"Space\",\"retry\":\"true\"}", "retry")]
    [InlineData("{\"theme\":\"Space\",\"extra\":true}", "extra")]
    public void Rejects_invalid_values(string json, string key)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var result = ParameterValidator.Validate(Schema, values);
        Assert.Contains($"parameters.{key}", result.Errors.Keys);
    }

    [Fact]
    public void Applies_defaults_without_mutating_input_and_preserves_false()
    {
        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"theme":"Space","retry":false}""")!;
        var result = ParameterValidator.Validate(Schema, input);
        Assert.Empty(result.Errors);
        Assert.Equal(5, result.Values["count"].GetInt32());
        Assert.False(result.Values["retry"].GetBoolean());
        Assert.False(input.ContainsKey("count"));
        Assert.False(result.Values.ContainsKey("level"));
    }

    [Theory]
    [InlineData("theme\n")]
    [InlineData("theme\r\n")]
    public void Template_parameter_keys_reject_trailing_newlines(string key)
    {
        var definition = new TaskTemplateDefinition(4, "Reading",
            [new(key, "Theme", "text")], new("Create reading questions.", new("Space", "Children", "easy", 2)));

        Assert.Contains("instanceParameters[0]", TemplateValidator.Validate(definition).Keys);
    }

    [Theory]
    [InlineData("text", "min")]
    [InlineData("select", "max")]
    [InlineData("boolean", "min")]
    [InlineData("integer", "maxLength")]
    [InlineData("select", "maxLength")]
    [InlineData("boolean", "maxLength")]
    [InlineData("text", "options")]
    [InlineData("integer", "options")]
    [InlineData("boolean", "options")]
    public void Template_rejects_settings_that_the_parameter_type_cannot_use(string type, string setting)
    {
        var field = new ParameterDefinition("choice", "Choice", type, Options: type == "select" ? ["one"] : null);
        field = setting switch
        {
            "min" => field with { Min = 1 },
            "max" => field with { Max = 10 },
            "maxLength" => field with { MaxLength = 100 },
            _ => field with { Options = ["unused"] }
        };
        var definition = new TaskTemplateDefinition(4, "Practice", [field], new("Use choice.", new("Space", "Children", "easy", 2)));

        Assert.Contains("instanceParameters[0]", TemplateValidator.Validate(definition).Keys);
    }

    [Theory]
    [InlineData(false, "{}", 4)]
    [InlineData(true, "{}", 4)]
    [InlineData(false, "{\"items\":6}", 6)]
    [InlineData(true, "{\"items\":6}", 6)]
    public void Integer_field_with_a_valid_default_resolves_omission_and_preserves_explicit_values(bool required, string json, int expected)
    {
        var field = new ParameterDefinition("items", "Items", "integer", required,
            JsonSerializer.SerializeToElement(4), Min: 3, Max: 6);
        var definition = new TaskTemplateDefinition(4, "Practice", [field], new("Use items.", new("Space", "Children", "easy", 2)));

        Assert.Empty(TemplateValidator.Validate(definition));
        var supplied = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var result = ParameterValidator.Validate(definition.InstanceParameters, supplied);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Values["items"].GetInt32());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("1.5")]
    [InlineData("\"4\"")]
    [InlineData("null")]
    public void Integer_field_rejects_an_invalid_default(string value)
    {
        var definition = new TaskTemplateDefinition(4, "Practice",
            [new("items", "Items", "integer", Default: JsonSerializer.Deserialize<JsonElement>(value), Min: 1, Max: 20)],
            new("Use items.", new("Space", "Children", "easy", 2)));

        Assert.Contains("instanceParameters[0]", TemplateValidator.Validate(definition).Keys);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(25, true)]
    public void Template_question_count_is_a_positive_integer(int count, bool valid)
    {
        var definition = new TaskTemplateDefinition(4, "Practice", [], new("Create questions.", new("Space", "Children", "easy", count)));
        Assert.Equal(!valid, TemplateValidator.Validate(definition).ContainsKey("generation.defaults.questionCount"));
    }
}
