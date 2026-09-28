using Microsoft.AspNetCore.Identity;

namespace FamilyLearning.Api.Infrastructure.Auth;

/// <summary>An Identity account whose family determines access to templates and drafts.</summary>
public sealed class ParentUser : IdentityUser
{
    public Guid FamilyId { get; set; }
}
