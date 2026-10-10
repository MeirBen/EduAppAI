namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>
/// The provider's concurrent-call budget, shared by interactive planning and the background worker. Each caller holds a
/// slot for its whole call: interactive requests never queue, while background work waits for its turn, so an accepted
/// operation never fails for local load.
/// </summary>
public sealed class AiCapacity : IDisposable
{
    private const int Slots = 2;
    private readonly SemaphoreSlim slots = new(Slots, Slots);

    /// <summary>A slot now, or null when the budget is in use; the caller replies busy.</summary>
    public IDisposable? TryEnter() => slots.Wait(0) ? new Slot(slots) : null;

    /// <summary>Waits for a slot; cancellation leaves the budget untouched.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await slots.WaitAsync(ct);
        return new Slot(slots);
    }

    public void Dispose() => slots.Dispose();

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) slots.Release();
        }
    }
}
