namespace FamilyLearning.Api.Features.Children;

/// <summary>Fixed-lifetime device access. Revocation is permanent and retains the original timestamp on replay.</summary>
public sealed class ChildDeviceGrant(Guid childId, string deviceLabel, DateTime createdAtUtc, DateTime expiresAtUtc)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ChildId { get; private set; } = childId;
    public string DeviceLabel { get; private set; } = deviceLabel;
    public DateTime CreatedAtUtc { get; private set; } = createdAtUtc;
    public DateTime ExpiresAtUtc { get; private set; } = expiresAtUtc;
    public DateTime? RevokedAtUtc { get; private set; }

    public void Revoke(DateTime utcNow) => RevokedAtUtc ??= utcNow;
}
