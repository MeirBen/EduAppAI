using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Api.Infrastructure.Auth;

/// <summary>Adds the server-owned family and parent-role claims when Identity creates a principal.</summary>
public sealed class ParentPrincipalFactory(UserManager<ParentUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ParentUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ParentUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new("family_id", user.FamilyId.ToString()));
        identity.AddClaim(new(ClaimTypes.Role, "Parent"));
        return identity;
    }
}

/// <summary>Access to claims issued by the application's parent principal factory.</summary>
public static class ParentIdentity
{
    /// <summary>Reads the family ID after the Parent authorization policy has succeeded.</summary>
    /// <remarks>Requires an application-issued principal with a valid GUID claim; this is not an input validator.</remarks>
    public static Guid FamilyId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("family_id")!);
}
