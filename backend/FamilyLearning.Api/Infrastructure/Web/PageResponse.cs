namespace FamilyLearning.Api.Infrastructure.Web;

/// <summary>A bounded page without an expensive total count; HasMore indicates another page is available.</summary>
public sealed record PageResponse<T>(T[] Items, int Page, int PageSize, bool HasMore)
{
    internal static PageResponse<T> From(List<T> rows, int page, int pageSize) =>
        new(rows.Take(pageSize).ToArray(), page, pageSize, rows.Count > pageSize);
}

/// <summary>Validates page bounds before computing a database offset. New collections default to 25 rows.</summary>
internal readonly record struct PageRequest(int Page, int PageSize)
{
    internal int Offset => (Page - 1) * PageSize;
    internal bool IsValid => Page > 0 && PageSize is >= 1 and <= 100 && (long)(Page - 1) * PageSize <= int.MaxValue;
    internal static IResult Invalid() => Results.ValidationProblem(new Dictionary<string, string[]>
    { ["page"] = ["יש לבחור עמוד חיובי וגודל עמוד בין 1 ל־100."] });
}
