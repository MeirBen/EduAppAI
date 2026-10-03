using System.Text;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine;

/// <summary>Shared Unicode word measurement for runtime validation and offline evaluation.</summary>
public static class TextLength
{
    /// <summary>Counts whitespace-separated tokens containing a Unicode letter or number; never rewrites the text.</summary>
    public static int CountWords(string text)
    {
        var count = 0;
        var countedToken = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune)) countedToken = false;
            else if (!countedToken && (Rune.IsLetter(rune) || Rune.IsNumber(rune)))
            {
                count++;
                countedToken = true;
            }
        }
        return count;
    }

    /// <summary>Measures generated bodies only, ignoring supplied sources, titles, instructions and questions.</summary>
    public static LengthMeasurement[] Measure(ResolvedTaskRequest request, TaskDocument document)
    {
        var result = new List<LengthMeasurement>();
        var total = 0;
        foreach (var material in request.Materials)
        {
            if (material.Source != "generated") continue;
            var content = document.Materials.FirstOrDefault(m => m.Id == material.Id);
            var actual = content is null ? 0 : CountWords(content.Body);
            total = checked(total + actual);
            if (material.Length is { } expected) result.Add(Measurement(material.Id, expected, actual));
        }
        if (request.TotalLength is { } totalLength) result.Add(Measurement("total", totalLength, total));
        return result.ToArray();
    }

    private static LengthMeasurement Measurement(string scope, ResolvedLength expected, int actual) => new(scope, expected, actual,
        expected.Mode == "range" ? actual >= expected.Lower && actual <= expected.Upper : null);
}
