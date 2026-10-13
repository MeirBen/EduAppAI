using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Infrastructure.Persistence;

internal static class PersistenceConfiguration
{
    internal static void AddLearningPersistence(this IServiceCollection services, string dataDirectory) =>
        services.AddDbContext<LearningDbContext>(options => options.UseSqlite(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "family-learning.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString()));

    internal static async Task MigrateLearningDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LearningDbContext>().Database.MigrateAsync();
    }
}
