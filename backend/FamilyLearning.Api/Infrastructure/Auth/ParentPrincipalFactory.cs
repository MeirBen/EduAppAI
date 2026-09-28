using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Api.Infrastructure.Auth;

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

public static class ParentIdentity
{
    public static Guid FamilyId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("family_id")!);
}
