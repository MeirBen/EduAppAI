using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Infrastructure.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Children;

/// <summary>Parent-owned profile and device management; all state-dependent writes serialize in SQLite.</summary>
public static class ChildEndpoints
{
    public static void MapChildEndpoints(this RouteGroupBuilder api)
    {
        var children = api.MapGroup("/children");
        children.MapGet("/", ListAsync);
        children.MapPost("/", async (CreateChildRequest request, ClaimsPrincipal user, LearningDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!ChildValidation.ValidName(request.Name)) return ChildValidation.InvalidName("name");
            var child = new Child(user.FamilyId(), request.Name.Trim(), clock.GetUtcNow().UtcDateTime);
            db.Children.Add(child);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/children/{child.Id}", ChildSummary.From(child));
        });
        children.MapPut("/{id:guid}", UpdateAsync);
        children.MapPost("/{id:guid}/activation", IssueAsync).RequireRateLimiting("child-issuance");
        children.MapGet("/{id:guid}/devices", DevicesAsync);
        children.MapDelete("/{id:guid}/devices/{grantId:guid}", RevokeAsync);
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, LearningDbContext db, CancellationToken ct, int page = 1, int pageSize = 25)
    {
        var paging = new PageRequest(page, pageSize);
        if (!paging.IsValid) return PageRequest.Invalid();
        var rows = await db.Children.AsNoTracking().Where(c => c.FamilyId == user.FamilyId())
            .OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id)
            .Select(c => new ChildSummary(c.Id, c.Name, c.Enabled, c.Revision, c.CreatedAtUtc))
            .Skip(paging.Offset).Take(pageSize + 1).ToListAsync(ct);
        return Results.Ok(PageResponse<ChildSummary>.From(rows, page, pageSize));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateChildRequest request, ClaimsPrincipal user, LearningDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var child = await db.Children.SingleOrDefaultAsync(c => c.Id == id && c.FamilyId == user.FamilyId(), ct);
        if (child is null) return Results.NotFound();
        if (!ChildValidation.ValidName(request.Name)) return ChildValidation.InvalidName("name");
        if (request.ExpectedRevision != child.Revision) return Results.Problem(statusCode: 409, title: "פרטי הילד השתנו. יש לטעון אותם מחדש.");
        child.Update(request.Name.Trim(), request.Enabled);
        if (!child.Enabled)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await db.ChildActivations.Where(a => a.ChildId == id).ExecuteDeleteAsync(ct);
            await db.ChildDeviceGrants.Where(g => g.ChildId == id && g.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAtUtc, now), ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(ChildSummary.From(child));
    }

    private static async Task<IResult> IssueAsync(Guid id, CreateActivationRequest request, ClaimsPrincipal user,
        LearningDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var child = await db.Children.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.FamilyId == user.FamilyId(), ct);
        if (child is null) return Results.NotFound();
        if (!ChildValidation.ValidName(request.DeviceLabel)) return ChildValidation.InvalidName("deviceLabel");
        if (!child.Enabled) return Results.Problem(statusCode: 409, title: "יש להפעיל את פרופיל הילד לפני יצירת קוד.");
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var hash = HashCode(code);
        var expires = clock.GetUtcNow().UtcDateTime.AddMinutes(10);
        var activation = await db.ChildActivations.SingleOrDefaultAsync(a => a.ChildId == id, ct);
        if (activation is null) db.ChildActivations.Add(new ChildActivation(id, hash, request.DeviceLabel.Trim(), expires));
        else activation.Replace(hash, request.DeviceLabel.Trim(), expires);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new ChildActivationCode(code, expires));
    }

    private static async Task<IResult> DevicesAsync(Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct,
        int page = 1, int pageSize = 25)
    {
        var paging = new PageRequest(page, pageSize);
        if (!paging.IsValid) return PageRequest.Invalid();
        if (!await db.Children.AnyAsync(c => c.Id == id && c.FamilyId == user.FamilyId(), ct)) return Results.NotFound();
        var rows = await db.ChildDeviceGrants.AsNoTracking().Where(g => g.ChildId == id)
            .OrderByDescending(g => g.CreatedAtUtc).ThenByDescending(g => g.Id)
            .Select(g => new ChildDeviceSummary(g.Id, g.DeviceLabel, g.CreatedAtUtc, g.ExpiresAtUtc, g.RevokedAtUtc))
            .Skip(paging.Offset).Take(pageSize + 1).ToListAsync(ct);
        return Results.Ok(PageResponse<ChildDeviceSummary>.From(rows, page, pageSize));
    }

    private static async Task<IResult> RevokeAsync(Guid id, Guid grantId, ClaimsPrincipal user, LearningDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var grant = await (from device in db.ChildDeviceGrants
                           join child in db.Children on device.ChildId equals child.Id
                           where child.Id == id && child.FamilyId == user.FamilyId() && device.Id == grantId
                           select device).SingleOrDefaultAsync(ct);
        if (grant is null) return Results.NotFound();
        grant.Revoke(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    internal static string HashCode(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
