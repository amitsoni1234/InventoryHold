namespace InventoryHold.Domain.Commands;

public sealed record CreateHoldLine(string ProductId, int Quantity);

public sealed record CreateHoldCommand(IReadOnlyList<CreateHoldLine>? Items, string? ClientRequestId);
