using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ParameterValidationTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(-1, 150, false)]
    [InlineData(100, 99, false)]
    [InlineData(100, 4001, false)]
    [InlineData(null, -1, false)]
    [InlineData(0, 0, true)]
    [InlineData(100, null, true)]
    [InlineData(null, 150, true)]
    public void Word_limits_require_a_bounded_ordered_range(int? min, int? max, bool valid)
    {
        var definition = new TaskTemplateDefinition(2, "Practice", [], new("Generate a task.",
            ContentWordCount: new(min, max)));
        Assert.Equal(!valid, TemplateValidator.Validate(definition).ContainsKey("generation.contentWordCount"));
    }

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
        Assert.Contains(key, result.Errors.Keys);
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
        var definition = new TaskTemplateDefinition(2, "Reading",
            [new(key, "Theme", "text")], new("Create reading questions."));

        Assert.Contains("instanceParameters[0]", TemplateValidator.Validate(definition).Keys);
    }

    [Theory]
    [InlineData(false, "{}", 4)]
    [InlineData(true, "{}", 4)]
    [InlineData(false, "{\"items\":6}", 6)]
    [InlineData(true, "{\"items\":6}", 6)]
    public void Bound_count_with_a_valid_default_resolves_omission_and_preserves_explicit_values(bool required, string json, int expected)
    {
        var field = new ParameterDefinition("items", "Items", "integer", required,
            JsonSerializer.SerializeToElement(4), Min: 3, Max: 6);
        var definition = new TaskTemplateDefinition(2, "Practice", [field], new("Use items.", "items"));

        Assert.Empty(TemplateValidator.Validate(definition));
        var supplied = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var result = ParameterValidator.Validate(definition.InstanceParameters, supplied);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Values["items"].GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bound_count_without_a_default_must_require_parent_input(bool required)
    {
        var definition = new TaskTemplateDefinition(2, "Practice",
            [new("items", "Items", "integer", required, Min: 1, Max: 20)], new("Use items.", "items"));

        Assert.Equal(!required, TemplateValidator.Validate(definition).ContainsKey("generation.questionCountParameter"));
        var missing = ParameterValidator.Validate(definition.InstanceParameters, new Dictionary<string, JsonElement>());
        Assert.Equal(required, missing.Errors.ContainsKey("items"));
        var supplied = ParameterValidator.Validate(definition.InstanceParameters,
            new Dictionary<string, JsonElement> { ["items"] = JsonSerializer.SerializeToElement(1) });
        Assert.Empty(supplied.Errors);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("1.5")]
    [InlineData("\"4\"")]
    [InlineData("null")]
    public void Bound_count_never_accepts_an_invalid_default(string value)
    {
        var definition = new TaskTemplateDefinition(2, "Practice",
            [new("items", "Items", "integer", Default: JsonSerializer.Deserialize<JsonElement>(value), Min: 1, Max: 20)],
            new("Use items.", "items"));

        Assert.Contains("instanceParameters[0]", TemplateValidator.Validate(definition).Keys);
    }

    [Theory]
    [InlineData("missing", "integer", 1, 20)]
    [InlineData("items", "text", 1, 20)]
    [InlineData("items", "integer", null, 20)]
    [InlineData("items", "integer", 1, null)]
    [InlineData("items", "integer", 0, 20)]
    [InlineData("items", "integer", 1, 21)]
    public void Bound_count_still_requires_an_existing_bounded_integer(string binding, string type, int? min, int? max)
    {
        var definition = new TaskTemplateDefinition(2, "Practice",
            [new("items", "Items", type, Default: JsonSerializer.SerializeToElement(4), Min: min, Max: max)],
            new("Use items.", binding));

        Assert.Contains("generation.questionCountParameter", TemplateValidator.Validate(definition).Keys);
    }
}
