namespace InventoryHold.WebApi.Configuration;

public sealed class HoldSettings
{
    public const string SectionName = "Holds";

    public int DurationMinutes { get; set; } = 15;

    public int ExpirationPollSeconds { get; set; } = 15;
}
