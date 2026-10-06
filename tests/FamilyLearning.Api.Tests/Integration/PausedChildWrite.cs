using System.Data.Common;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyLearning.Api.Tests.Integration;

/// <summary>Pauses one write before SQLite takes its lock, after HTTP authentication/CSRF have completed.</summary>
internal sealed class PausedChildWrite : DbTransactionInterceptor
{
    private int armed;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void Arm() => Interlocked.Exchange(ref armed, 1);
    internal void Configure(IServiceCollection services) => services.AddScoped(provider => new LearningDbContext(
        new DbContextOptionsBuilder<LearningDbContext>(provider.GetRequiredService<DbContextOptions<LearningDbContext>>())
            .AddInterceptors(this).Options));

    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
        TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref armed, 0) == 1)
        {
            Entered.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
        }
        return result;
    }
}
