using System.Threading.Channels;

namespace FamilyLearning.Api.Features.Library;

/// <summary>
/// Process-local fan-out of committed library writes to the open change streams of the family that made them.
/// A note carries no content. The API deploys as one process, so no cross-process delivery exists.
/// </summary>
public sealed class LibraryChanges
{
    /// <summary>Open streams per family; a further stream is refused until one closes or expires.</summary>
    public const int FamilyStreamLimit = 16;
    /// <summary>Bounds each stream so reconnecting re-runs authentication, and an unnoticed dead connection releases its slot.</summary>
    public static readonly TimeSpan StreamLifetime = TimeSpan.FromMinutes(5);

    // One pending note per stream: further changes coalesce, so a slow reader never queues memory.
    private static readonly BoundedChannelOptions SingleNote = new(1) { FullMode = BoundedChannelFullMode.DropWrite };
    private readonly Dictionary<Guid, List<Channel<bool>>> families = [];

    /// <summary>Notifies the family's open streams; call only after the write commits. Never waits for a stream.</summary>
    public void Publish(Guid familyId)
    {
        lock (families)
            if (families.TryGetValue(familyId, out var streams))
                foreach (var stream in streams) stream.Writer.TryWrite(true);
    }

    /// <summary>Opens a stream, or returns null when the family already holds <see cref="FamilyStreamLimit"/> streams.</summary>
    /// <remarks>Its first note covers changes committed before it subscribed. Pass it to <see cref="Unsubscribe"/> when done.</remarks>
    public Channel<bool>? Subscribe(Guid familyId)
    {
        lock (families)
        {
            if (!families.TryGetValue(familyId, out var streams)) families[familyId] = streams = [];
            else if (streams.Count >= FamilyStreamLimit) return null;
            var stream = Channel.CreateBounded<bool>(SingleNote);
            stream.Writer.TryWrite(true);
            streams.Add(stream);
            return stream;
        }
    }

    /// <summary>Closes the stream and releases its family slot.</summary>
    public void Unsubscribe(Guid familyId, Channel<bool> stream)
    {
        lock (families)
        {
            var streams = families[familyId];
            streams.Remove(stream);
            if (streams.Count == 0) families.Remove(familyId);
        }
    }
}
