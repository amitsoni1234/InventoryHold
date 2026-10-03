namespace InventoryHold.Contracts;

public sealed class ApiError
{
    public required string ErrorCode { get; init; }

    public required string Message { get; init; }
}
