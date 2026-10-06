namespace FamilyLearning.Api.Features.Children;

/// <summary>One replaceable activation slot per child. Only the hash is stored; consumption never permits replay.</summary>
public sealed class ChildActivation(Guid childId, string codeHash, string deviceLabel, DateTime expiresAtUtc)
{
    public Guid ChildId { get; private set; } = childId;
    public string CodeHash { get; private set; } = codeHash;
    public string DeviceLabel { get; private set; } = deviceLabel;
    public DateTime ExpiresAtUtc { get; private set; } = expiresAtUtc;
    public DateTime? ConsumedAtUtc { get; private set; }

    public void Replace(string codeHash, string deviceLabel, DateTime expiresAtUtc)
    {
        CodeHash = codeHash;
        DeviceLabel = deviceLabel;
        ExpiresAtUtc = expiresAtUtc;
        ConsumedAtUtc = null;
    }

    public void Consume(DateTime utcNow) => ConsumedAtUtc = utcNow;
}
