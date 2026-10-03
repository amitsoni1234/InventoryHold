namespace InventoryHold.Domain;

public static class ErrorCodes
{
    public const string Validation = "VALIDATION_ERROR";
    public const string DuplicateProduct = "DUPLICATE_PRODUCT";
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string HoldNotFound = "HOLD_NOT_FOUND";
    public const string HoldAlreadyReleased = "HOLD_ALREADY_RELEASED";
    public const string HoldExpired = "HOLD_EXPIRED";
    public const string HoldNotActive = "HOLD_NOT_ACTIVE";
}
