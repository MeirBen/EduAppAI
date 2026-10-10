using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Infrastructure.Web;

/// <summary>A bounded page without an expensive total count; HasMore indicates another page is available.</summary>
public sealed record PageResponse<T>(T[] Items, int Page, int PageSize, bool HasMore);

/// <summary>Validates page bounds before computing a database offset. Lists default to 25 rows.</summary>
internal readonly record struct PageRequest(int Page, int PageSize)
{
    internal int Offset => (Page - 1) * PageSize;
    internal bool IsValid => Page > 0 && PageSize is >= 1 and <= 100 && (long)(Page - 1) * PageSize <= int.MaxValue;
    internal static IResult Invalid() => Results.ValidationProblem(new Dictionary<string, string[]>
    { ["page"] = ["יש לבחור עמוד חיובי וגודל עמוד בין 1 ל־100."] });

    /// <summary>Reads one page of an ordered query; one extra row tells whether another page follows.</summary>
    internal async Task<PageResponse<T>> ReadAsync<T>(IQueryable<T> ordered, CancellationToken ct)
    {
        var rows = await ordered.Skip(Offset).Take(PageSize + 1).ToListAsync(ct);
        return new(rows.Take(PageSize).ToArray(), Page, PageSize, rows.Count > PageSize);
    }
}
