using System.Collections.Generic;
using System.Net.Http;

namespace SESARWebHook.Core.Models
{
  /// <summary>
  /// Represents a request to push data to an external API.
  /// Returned by IWebhookHandler.TransformAsync() to tell the GenericConnector
  /// what to send and where.
  ///
  /// MINIMAL EXAMPLE:
  /// <code>
  /// new PushRequest
  /// {
  ///     TargetUrl = "https://api.example.com/webhook",
  ///     Payload = new { name = "John", email = "john@example.com" }
  /// }
  /// </code>
  ///
  /// FULL EXAMPLE WITH AUTH:
  /// <code>
  /// new PushRequest
  /// {
  ///     TargetUrl = "https://api.example.com/contacts",
  ///     Method = HttpMethod.Post,
  ///     Payload = new { name = "John", email = "john@example.com" },
  ///     Headers = new Dictionary&lt;string, string&gt;
  ///     {
  ///         { "X-Custom-Header", "value" }
  ///     },
  ///     Authentication = new AuthenticationConfig
  ///     {
  ///         Type = AuthenticationType.Bearer,
  ///         Token = "your-api-token"
  ///     }
  /// }
  /// </code>
  /// </summary>
  public class PushRequest
  {
    /// <summary>
    /// The target URL to send the request to.
    /// Required.
    /// </summary>
    public string TargetUrl { get; set; }

    /// <summary>
    /// HTTP method to use. Defaults to POST.
    /// Supported: POST, PUT, PATCH
    /// </summary>
    public HttpMethod Method { get; set; } = HttpMethod.Post;

    /// <summary>
    /// The payload to send. Will be serialized to JSON.
    /// Can be an anonymous object, a class instance, or a Dictionary.
    /// </summary>
    public object Payload { get; set; }

    /// <summary>
    /// Optional custom headers to include in the request.
    /// Note: Content-Type is automatically set to application/json.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; }

    /// <summary>
    /// Optional authentication configuration.
    /// If not set, no authentication is added to the request.
    /// </summary>
    public AuthenticationConfig Authentication { get; set; }

    /// <summary>
    /// Optional: Content type override.
    /// Defaults to "application/json".
    /// </summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// Optional: Timeout in seconds for this specific request.
    /// Defaults to 30 seconds if not specified.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Optional: Number of retry attempts on failure.
    /// Defaults to 3 if not specified.
    /// </summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Optional: Skip SSL certificate validation.
    /// WARNING: Only use for development/testing. Never in production.
    /// </summary>
    public bool SkipSslValidation { get; set; } = false;

    /// <summary>
    /// Optional: Custom metadata to pass through to OnSuccessAsync/OnErrorAsync.
    /// Use this to track request-specific data.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; }

    public PushRequest()
    {
      Headers = new Dictionary<string, string>();
      Metadata = new Dictionary<string, object>();
    }
  }

  /// <summary>
  /// Authentication configuration for the push request
  /// </summary>
  public class AuthenticationConfig
  {
    /// <summary>
    /// Type of authentication to use
    /// </summary>
    public AuthenticationType Type { get; set; }

    /// <summary>
    /// Token for Bearer authentication
    /// </summary>
    public string Token { get; set; }

    /// <summary>
    /// API Key value (for ApiKey authentication)
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Header name for API Key. Defaults to "X-API-Key".
    /// </summary>
    public string ApiKeyHeader { get; set; } = "X-API-Key";

    /// <summary>
    /// Username for Basic authentication
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Password for Basic authentication
    /// </summary>
    public string Password { get; set; }
  }

  /// <summary>
  /// Supported authentication types
  /// </summary>
  public enum AuthenticationType
  {
    /// <summary>
    /// No authentication
    /// </summary>
    None = 0,

    /// <summary>
    /// Bearer token in Authorization header
    /// </summary>
    Bearer = 1,

    /// <summary>
    /// API Key in custom header
    /// </summary>
    ApiKey = 2,

    /// <summary>
    /// Basic authentication (username:password)
    /// </summary>
    Basic = 3
  }
}
