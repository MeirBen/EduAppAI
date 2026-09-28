using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace FamilyLearning.Api.Infrastructure.Auth;

public sealed class ParentAccount(LearningDbContext db, UserManager<ParentUser> users)
{
    public async Task<IdentityResult> CreateAsync(string email, string password)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var family = new Family();
        db.Families.Add(family);
        await db.SaveChangesAsync();
        var user = new ParentUser { UserName = email.Trim(), Email = email.Trim(), FamilyId = family.Id };
        var result = await users.CreateAsync(user, password);
        if (result.Succeeded) await transaction.CommitAsync();
        return result;
    }
}
