namespace FamilyLearning.Api.Features;

/// <summary>A bounded page without an expensive total count; HasMore indicates another page is available.</summary>
public sealed record PageResponse<T>(T[] Items, int Page, int PageSize, bool HasMore);

/// <summary>Validates page bounds before computing a database offset.</summary>
internal readonly record struct PageRequest(int Page, int PageSize)
{
    internal const int DefaultSize = 5;
    internal int Offset => (Page - 1) * PageSize;
    internal bool IsValid => Page > 0 && PageSize is >= 1 and <= 100 && (long)(Page - 1) * PageSize <= int.MaxValue;
    internal static FeatureFailure Invalid() => FeatureFailure.Invalid("page", "יש לבחור עמוד חיובי וגודל עמוד בין 1 ל־100.");
}
