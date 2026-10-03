namespace InventoryHold.Domain.Entities;

public sealed class Hold
{
    public Hold(
        string holdId,
        HoldStatus status,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        IReadOnlyList<HoldItem> items,
        string? clientRequestId)
    {
        if (string.IsNullOrWhiteSpace(holdId))
        {
            throw new ArgumentException("Hold id is required.", nameof(holdId));
        }

        HoldId = holdId;
        Status = status;
        CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        ExpiresAtUtc = DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc);
        Items = items ?? throw new ArgumentNullException(nameof(items));
        ClientRequestId = string.IsNullOrWhiteSpace(clientRequestId) ? null : clientRequestId.Trim();
    }

    public string HoldId { get; }

    public HoldStatus Status { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime ExpiresAtUtc { get; }

    public IReadOnlyList<HoldItem> Items { get; }

    public string? ClientRequestId { get; }

    public bool IsActiveAndDue(DateTime utcNow) =>
        Status == HoldStatus.Active && ExpiresAtUtc <= utcNow;

    public Hold WithStatus(HoldStatus status) =>
        new(HoldId, status, CreatedAtUtc, ExpiresAtUtc, Items, ClientRequestId);
}
