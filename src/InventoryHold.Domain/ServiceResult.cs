namespace InventoryHold.Domain;

public sealed record ServiceError(string Code, string Message, int StatusCode);

public sealed class ServiceResult<T>
{
    private ServiceResult(T? value, ServiceError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public ServiceError? Error { get; }

    public bool IsSuccess => Error is null;

    public static ServiceResult<T> Ok(T value) => new(value, null);

    public static ServiceResult<T> Fail(string code, string message, int statusCode) =>
        new(default, new ServiceError(code, message, statusCode));
}
