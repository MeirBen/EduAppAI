namespace FamilyLearning.Api.Features.Activities;

/// <summary>Single-process queue and evidence budgets. Polling affects latency only; SQLite owns pending work.</summary>
public sealed class GenerationOperationOptions
{
    public const int GlobalLimit = 32;
    public const int FamilyLimit = 4;
    public const int DraftLimit = 128;
    public const int StepLimit = 8;
    public const int EvidenceByteLimit = 2 * 1024 * 1024;
    // Reserve room for all bounded call summaries even when bulky evidence fills its allowance.
    internal const int SummaryByteLimit = 16 * 1024;
    public const int PurgeBatchSize = 32;
    public static readonly TimeSpan ArtifactRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);
    public int PollIntervalSeconds { get; set; } = 1;
}
