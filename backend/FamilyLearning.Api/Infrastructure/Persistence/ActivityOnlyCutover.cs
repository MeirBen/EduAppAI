using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FamilyLearning.Api.Infrastructure.Persistence;

/// <summary>Explicit one-time learning reset for the activity-only schema. Run with the app and worker stopped.</summary>
internal static class ActivityOnlyCutover
{
    private const string Migration = "20261009124947_ActivityOnly";
    private const string PreviousMigration = "20261009111918_RemoveUnusedActivityState";

    /// <summary>Deletes learning records transactionally, retaining Identity and families, then migrates the empty schema.</summary>
    /// <returns>False if the cutover is already applied; subsequent activity-only work is never reset.</returns>
    /// <remarks>A schema failure after the committed reset leaves empty learning data; rerun explicitly to finish migration.</remarks>
    internal static async Task<bool> RunAsync(LearningDbContext db, CancellationToken ct = default)
    {
        if (!(await db.Database.GetPendingMigrationsAsync(ct)).Contains(Migration)) return false;
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // The predecessor's fixed schema is intentional: no old plan or operation JSON is read or converted.
            await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM TaskSessions;
                DELETE FROM Assignments;
                DELETE FROM ChildDeviceGrants;
                DELETE FROM ChildActivations;
                DELETE FROM Children;
                DELETE FROM GenerationOperations;
                DELETE FROM ActivityDrafts;
                DELETE FROM TaskSnapshots;
                DELETE FROM TaskTemplateVersions;
                DELETE FROM TaskTemplates;
                """, ct);
            await transaction.CommitAsync(ct);
        }
        await db.Database.MigrateAsync(ct);
        return true;
    }
}
