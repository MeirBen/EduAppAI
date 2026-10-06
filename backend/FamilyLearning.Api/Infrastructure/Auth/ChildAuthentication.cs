using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace FamilyLearning.Api.Infrastructure.Auth;

/// <summary>Independent child cookie validation and explicit session-entry rules for separate-device access.</summary>
public static class ChildAuthentication
{
    public const string Scheme = "Child";
    public const string SessionConflictType = "urn:family-learning:device-session-conflict";

    internal static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        if (await ChildAccess.FindAsync(context.Principal!, services.GetRequiredService<LearningDbContext>(),
            services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, context.HttpContext.RequestAborted) is null)
            context.RejectPrincipal();
    }

    internal static ClaimsPrincipal Principal(ChildIdentity identity) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, identity.ChildId.ToString()),
        new Claim(ClaimTypes.Role, "Child"),
        new Claim("family_id", identity.FamilyId.ToString()),
        new Claim("device_grant_id", identity.DeviceGrantId.ToString())
    ], Scheme));

    /// <summary>Selects the intended principal before CSRF and rejects session replacement without issuing tokens.</summary>
    internal static async Task<IResult?> SelectEntryAsync(HttpContext context, DeviceSessionEntry entry)
    {
        var opposite = entry.Scheme == Scheme ? IdentityConstants.ApplicationScheme : Scheme;
        if ((await context.AuthenticateAsync(opposite)).Succeeded)
            return Conflict();
        var intended = await context.AuthenticateAsync(entry.Scheme);
        if (entry.RequireAnonymous && intended.Succeeded) return Conflict();
        context.User = intended.Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
        return null;
    }

    private static IResult Conflict() => Results.Problem(statusCode: 409, type: SessionConflictType,
        title: "כבר קיימת כניסה פעילה בדפדפן. יש להתנתק ממנה או להשתמש בדפדפן נפרד.");
}

/// <summary>Session-entry endpoint metadata; activation requires no active identity of either kind.</summary>
internal sealed record DeviceSessionEntry(string Scheme, bool RequireAnonymous = false);
