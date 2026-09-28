using Microsoft.AspNetCore.Identity;

namespace FamilyLearning.Api.Infrastructure.Auth;

public sealed class ParentUser : IdentityUser
{
    public Guid FamilyId { get; set; }
}
