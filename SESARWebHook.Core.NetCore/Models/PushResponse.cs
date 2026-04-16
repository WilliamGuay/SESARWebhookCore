using System.Collections.Generic;
using System.Net;

namespace SESARWebHook.Core.Models
{
  /// <summary>
  /// Response from a successful push operation.
  /// Passed to IWebhookHandler.OnSuccessAsync() for post-processing.
  /// </summary>
  public class PushResponse
  {
    /// <summary>
    /// HTTP status code from the target API
    /// </summary>
    public HttpStatusCode StatusCode { get; set; }

    /// <summary>
    /// Raw response body from the target API
    /// </summary>
    public string Body { get; set; }

    /// <summary>
    /// Response headers from the target API
    /// </summary>
    public Dictionary<string, string> Headers { get; set; }

    /// <summary>
    /// Time taken for the request in milliseconds
    /// </summary>
    public long ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Number of retry attempts made (0 if succeeded on first try)
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// The target URL that was called
    /// </summary>
    public string TargetUrl { get; set; }

    /// <summary>
    /// Custom metadata passed through from PushRequest
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; }

    public PushResponse()
    {
      Headers = new Dictionary<string, string>();
      Metadata = new Dictionary<string, object>();
    }

    /// <summary>
    /// Attempts to deserialize the response body as the specified type.
    /// Returns default(T) if deserialization fails.
    /// </summary>
    public T GetBodyAs<T>()
    {
      if (string.IsNullOrEmpty(Body))
        return default;

      try
      {
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(Body);
      }
      catch
      {
        return default;
      }
    }
  }

  /// <summary>
  /// Details about a failed push operation.
  /// Passed to IWebhookHandler.OnErrorAsync() for error handling.
  /// </summary>
  public class PushError
  {
    /// <summary>
    /// Type of error that occurred
    /// </summary>
    public PushErrorType ErrorType { get; set; }

    /// <summary>
    /// Human-readable error message
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// HTTP status code (if applicable)
    /// </summary>
    public HttpStatusCode? StatusCode { get; set; }

    /// <summary>
    /// Response body from the target API (if applicable)
    /// </summary>
    public string ResponseBody { get; set; }

    /// <summary>
    /// Exception details (if applicable)
    /// </summary>
    public string ExceptionDetails { get; set; }

    /// <summary>
    /// The target URL that was called
    /// </summary>
    public string TargetUrl { get; set; }

    /// <summary>
    /// Number of retry attempts made before giving up
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Total time spent attempting the request (including retries)
    /// </summary>
    public long TotalElapsedMilliseconds { get; set; }

    /// <summary>
    /// Custom metadata passed through from PushRequest
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; }

    public PushError()
    {
      Metadata = new Dictionary<string, object>();
    }

    /// <summary>
    /// Creates a PushError from an HTTP error response
    /// </summary>
    public static PushError FromHttpError(HttpStatusCode statusCode, string responseBody, string targetUrl)
    {
      return new PushError
      {
        ErrorType = PushErrorType.HttpError,
        Message = $"HTTP {(int)statusCode} {statusCode}",
        StatusCode = statusCode,
        ResponseBody = responseBody,
        TargetUrl = targetUrl
      };
    }

    /// <summary>
    /// Creates a PushError from an exception
    /// </summary>
    public static PushError FromException(System.Exception ex, string targetUrl)
    {
      var errorType = PushErrorType.Unknown;

      if (ex is System.Net.Http.HttpRequestException)
        errorType = PushErrorType.ConnectionError;
      else if (ex is System.Threading.Tasks.TaskCanceledException)
        errorType = PushErrorType.Timeout;
      else if (ex is System.Net.Sockets.SocketException)
        errorType = PushErrorType.ConnectionError;

      return new PushError
      {
        ErrorType = errorType,
        Message = ex.Message,
        ExceptionDetails = ex.ToString(),
        TargetUrl = targetUrl
      };
    }
  }

  /// <summary>
  /// Types of errors that can occur during a push operation
  /// </summary>
  public enum PushErrorType
  {
    /// <summary>
    /// Unknown error
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// HTTP error response (4xx, 5xx)
    /// </summary>
    HttpError = 1,

    /// <summary>
    /// Connection error (DNS, network, refused)
    /// </summary>
    ConnectionError = 2,

    /// <summary>
    /// Request timed out
    /// </summary>
    Timeout = 3,

    /// <summary>
    /// SSL/TLS certificate error
    /// </summary>
    SslError = 4,

    /// <summary>
    /// Authentication failed
    /// </summary>
    AuthenticationError = 5,

    /// <summary>
    /// Serialization error (payload could not be serialized)
    /// </summary>
    SerializationError = 6,

    /// <summary>
    /// Configuration error (missing required settings)
    /// </summary>
    ConfigurationError = 7
  }
}
