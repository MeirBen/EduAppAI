using FamilyLearning.Api.Features.Auth;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Children;

/// <summary>Child-only device activation, identity discovery and disconnect; no parent DTOs or AI calls.</summary>
public static class ChildAuthEndpoints
{
    public static void MapChildAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapGet("/csrf", AuthEndpoints.Csrf).AllowAnonymous().WithMetadata(new DeviceSessionEntry(ChildAuthentication.Scheme));
        auth.MapPost("/activate", ActivateAsync).AllowAnonymous().RequireRateLimiting("child-redemption")
            .WithMetadata(new DeviceSessionEntry(ChildAuthentication.Scheme, RequireAnonymous: true));
        auth.MapGet("/me", async (HttpContext context, LearningDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var identity = await ChildAccess.FindAsync(context.User, db, clock.GetUtcNow().UtcDateTime, ct);
            if (identity is null) return Results.Unauthorized();
            var result = await (from child in db.Children.AsNoTracking()
                                join grant in db.ChildDeviceGrants.AsNoTracking() on child.Id equals grant.ChildId
                                where child.Id == identity.ChildId && grant.Id == identity.DeviceGrantId
                                select new ChildSessionIdentity(child.Id, child.Name, grant.ExpiresAtUtc, EngineValidation.AnswerLength)).SingleOrDefaultAsync(ct);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        });
        auth.MapPost("/logout", LogoutAsync);
    }

    private static async Task<IResult> ActivateAsync(ActivateChildRequest request, HttpContext context, LearningDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        if (request.Code is null || request.Code.Length != 22) return InvalidCode();
        var hash = ChildEndpoints.HashCode(request.Code);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var activation = await db.ChildActivations.SingleOrDefaultAsync(a => a.CodeHash == hash &&
            a.ConsumedAtUtc == null && a.ExpiresAtUtc > now, ct);
        if (activation is null) return InvalidCode();
        var child = await db.Children.AsNoTracking().SingleOrDefaultAsync(c => c.Id == activation.ChildId && c.Enabled, ct);
        if (child is null) return InvalidCode();
        activation.Consume(now);
        var grant = new ChildDeviceGrant(child.Id, activation.DeviceLabel, now, now.AddDays(30));
        db.ChildDeviceGrants.Add(grant);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await context.SignInAsync(ChildAuthentication.Scheme, ChildAuthentication.Principal(new(child.Id, child.FamilyId, grant.Id)),
            new AuthenticationProperties { IsPersistent = true, IssuedUtc = new DateTimeOffset(now), ExpiresUtc = new DateTimeOffset(grant.ExpiresAtUtc), AllowRefresh = false });
        // The next /csrf read must obtain a token bound to the newly issued identity.
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, LearningDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var identity = await ChildAccess.FindAsync(context.User, db, now, ct);
        if (identity is null) return Results.Unauthorized();
        var grant = await db.ChildDeviceGrants.SingleAsync(g => g.Id == identity.DeviceGrantId, ct);
        grant.Revoke(now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await context.SignOutAsync(ChildAuthentication.Scheme);
        return Results.NoContent();
    }

    private static IResult InvalidCode() => Results.Problem(statusCode: 400, title: "קוד ההפעלה אינו תקף. יש לבקש קוד חדש מההורה.");
}
