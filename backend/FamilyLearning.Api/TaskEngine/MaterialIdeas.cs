using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.TaskEngine;

/// <summary>Contract and selection rules for the ideas proposed before material is written.</summary>
/// <remarks>Overlap is the model's estimate against recent family ideas; it does not prove novelty or quality.</remarks>
public static class MaterialIdeas
{
    public const int CandidateCount = 5;
    public const int TextLimit = 400;

    /// <summary>Selects a lowest-overlap idea from a validated batch; equal inputs always select the same idea.</summary>
    /// <remarks>Without relevant history every estimate ties and the first listed idea is the model's most typical one,
    /// so an application-owned <paramref name="draw"/> spreads the choice.</remarks>
    public static MaterialIdea Select(MaterialIdeaCandidateBatch candidates, int draw)
    {
        var lowest = candidates.Ideas.Min(candidate => candidate.RecentOverlap);
        var tied = candidates.Ideas.Where(candidate => candidate.RecentOverlap == lowest).ToArray();
        return tied[(int)((uint)draw % (uint)tied.Length)].Idea;
    }

    /// <summary>Requires exactly <see cref="CandidateCount"/> distinct, bounded ideas with overlap from 0 to 100.</summary>
    internal static void Validate(MaterialIdeaCandidateBatch candidates)
    {
        if (candidates?.Ideas is not { Length: CandidateCount } ideas) throw Invalid();
        var distinct = new HashSet<MaterialIdea>();
        foreach (var candidate in ideas)
            if (candidate?.Idea is not { } idea || !EngineValidation.HasText(idea.Premise, TextLimit) ||
                !EngineValidation.HasText(idea.Structure, TextLimit) || candidate.RecentOverlap is < 0 or > 100 ||
                !distinct.Add(new(idea.Premise.Trim(), idea.Structure.Trim()))) throw Invalid();
    }

    private static TaskValidationException Invalid() => new("ideas", "יש להציע רעיונות שונים בגודל ובמבנה נתמכים.");
}
