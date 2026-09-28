using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ParentAccountTests
{
    [Theory]
    [InlineData(254, 256, true)]
    [InlineData(254, 257, false)]
    [InlineData(255, 256, false)]
    public async Task Provisioning_matches_sign_in_credential_limits(int emailLength, int passwordLength, bool accepted)
    {
        using var app = new ApiFactory();
        using var scope = app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ParentAccount>()
            .CreateAsync(new string('a', emailLength - 13) + "@example.test",
                "Pass!1" + new string('x', passwordLength - 6));

        Assert.Equal(accepted, result.Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Equal(accepted ? 1 : 0, await db.Users.CountAsync());
        Assert.Equal(accepted ? 1 : 0, await db.Families.CountAsync());
    }
}
