using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class TextLengthTests
{
    [Theory]
    [InlineData("שלום עולם", 2)]
    [InlineData("שלום — עולם", 2)]
    [InlineData("בעלי־חיים", 1)]
    [InlineData("don't", 1)]
    [InlineData("🙂", 0)]
    [InlineData("Ⅳ ½", 2)]
    [InlineData("שָׁלוֹם\nHello\t123", 3)]
    [InlineData("𐐀 𐐨", 2)]
    [InlineData("\u05b0 -- \n", 0)]
    [InlineData("", 0)]
    public void Counts_only_whitespace_tokens_with_unicode_letters_or_numbers(string text, int expected) =>
        Assert.Equal(expected, TextLength.CountWords(text));

    [Fact]
    public void Measures_generated_bodies_only_without_inventing_target_tolerance()
    {
        var plan = Reading() with
        {
            Materials = [Reading().Materials[0] with { Length = null },
            Supplied().Materials[0] with { Id = OtherId }],
            TotalLength = new("target", new(120, true))
        };
        var request = Resolve(plan);
        var document = new TaskDocument("כותרת ארוכה", "הנחיות ארוכות",
            [new(MaterialId, 1, "כותרת החומר", "שלום — עולם", new("manual"), null),
             new(OtherId, 1, null, Source, new("supplied"), null)], []);
        var measurement = Assert.Single(TextLength.Measure(request, document));
        Assert.Equal(2, measurement.Actual);
        Assert.Equal("total", measurement.Scope);
        Assert.Null(measurement.Satisfied);
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(150, true)]
    [InlineData(151, false)]
    public void Historical_100_to_150_range_remains_strict(int words, bool expected)
    {
        var plan = Reading();
        plan = plan with { Materials = [plan.Materials[0] with { Length = new("range", Lower: 100, Upper: 150) }] };
        var document = new TaskDocument("", null, [new(MaterialId, 1, "ignored", string.Join(' ', Enumerable.Repeat("א", words)), new("manual"), null)], []);
        var result = Assert.Single(TextLength.Measure(Resolve(plan), document));
        Assert.Equal(expected, result.Satisfied);
    }
}
