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
}
