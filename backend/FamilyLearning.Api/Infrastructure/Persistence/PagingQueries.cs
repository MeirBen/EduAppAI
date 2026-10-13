using FamilyLearning.Api.Features;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Infrastructure.Persistence;

internal static class PagingQueries
{
    /// <summary>Reads one page of an ordered query; one extra row tells whether another page follows.</summary>
    internal static async Task<PageResponse<T>> ReadAsync<T>(this PageRequest page, IQueryable<T> ordered, CancellationToken ct)
    {
        var rows = await ordered.Skip(page.Offset).Take(page.PageSize + 1).ToListAsync(ct);
        return new(rows.Take(page.PageSize).ToArray(), page.Page, page.PageSize, rows.Count > page.PageSize);
    }
}
