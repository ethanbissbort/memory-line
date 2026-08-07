using System.Net;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.Sync;

/// <summary>
/// Raised when a call to the Memory Line Sync API fails. Carries the HTTP status
/// (when a response was received at all), the parsed <see cref="ApiError"/>
/// envelope when the response body contained one, and whether retrying the
/// identical call can succeed: 408/429/5xx and transport failures are
/// retryable, every other 4xx is not (design §16.2 retry classification).
/// </summary>
public class SyncApiException : Exception
{
    public SyncApiException(string message, HttpStatusCode? statusCode, ApiError? error, bool retryable)
        : this(message, statusCode, error, retryable, innerException: null)
    {
    }

    public SyncApiException(
        string message,
        HttpStatusCode? statusCode,
        ApiError? error,
        bool retryable,
        Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Error = error;
        Retryable = retryable;
    }

    /// <summary>HTTP status of the failed response, or null for transport-level failures.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The service's structured error envelope, when the body contained one.</summary>
    public ApiError? Error { get; }

    /// <summary>Whether retrying the identical request can succeed.</summary>
    public bool Retryable { get; }
}
