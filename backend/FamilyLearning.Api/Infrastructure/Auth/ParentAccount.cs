using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace FamilyLearning.Api.Infrastructure.Auth;

/// <summary>Provisions a parent and a new family using the same scoped Identity/EF context.</summary>
public sealed class ParentAccount(LearningDbContext db, UserManager<ParentUser> users)
{
    /// <summary>Maximum email length accepted by both provisioning and sign-in.</summary>
    public const int MaximumEmailLength = 254;
    /// <summary>Maximum password length accepted by both provisioning and sign-in.</summary>
    public const int MaximumPasswordLength = 256;

    /// <summary>Creates an account, rolling back the new family if Identity rejects the credentials.</summary>
    /// <returns>Identity's validation result; database failures propagate to the caller.</returns>
    /// <remarks>Use a fresh service scope for each provisioning operation; the context is not thread-safe.</remarks>
    public async Task<IdentityResult> CreateAsync(string email, string password)
    {
        email = email.Trim();
        if (email.Length > MaximumEmailLength || password.Length > MaximumPasswordLength)
            return IdentityResult.Failed(new IdentityError
            {
                Code = "CredentialsTooLong",
                Description = $"Email addresses must be at most {MaximumEmailLength} characters and passwords at most {MaximumPasswordLength}."
            });
        await using var transaction = await db.Database.BeginTransactionAsync();
        var family = new Family();
        db.Families.Add(family);
        await db.SaveChangesAsync();
        var user = new ParentUser { UserName = email, Email = email, FamilyId = family.Id };
        var result = await users.CreateAsync(user, password);
        if (result.Succeeded) await transaction.CommitAsync();
        return result;
    }
}
