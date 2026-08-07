namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// A failure outcome an application service maps to an ApiError response:
/// HTTP status, stable error code, safe human-readable message (never contains
/// secrets or personal content), and retryability (design §16.2).
/// </summary>
public sealed record ServiceError(int StatusCode, string Code, string Message, bool Retryable = false);

/// <summary>
/// Success-or-error result returned by application services so endpoints can
/// translate outcomes without exceptions carrying control flow.
/// </summary>
public sealed class ServiceResult<T>
{
    private ServiceResult(T? value, ServiceError? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>The success value; only meaningful when <see cref="Error"/> is null.</summary>
    public T? Value { get; }

    public ServiceError? Error { get; }

    public bool IsSuccess => Error is null;

    public static ServiceResult<T> Ok(T value) => new(value, null);

    public static ServiceResult<T> Fail(ServiceError error) => new(default, error);

    public static ServiceResult<T> Fail(int statusCode, string code, string message, bool retryable = false)
        => new(default, new ServiceError(statusCode, code, message, retryable));
}
