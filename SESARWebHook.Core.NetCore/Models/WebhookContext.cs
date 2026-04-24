using System;
using System.Collections.Generic;

namespace SESARWebHook.Core.Models
{
  /// <summary>
  /// Context information for a webhook request
  /// </summary>
  public class WebhookContext
  {
    /// <summary>
    /// Unique identifier for this webhook request
    /// </summary>
    public string RequestId { get; set; }

    /// <summary>
    /// Timestamp when the webhook was received
    /// </summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>
    /// Source IP address of the request
    /// </summary>
    public string SourceIp { get; set; }

    /// <summary>
    /// The connector ID configured for this request
    /// </summary>
    public string ConnectorId { get; set; }

    /// <summary>
    /// The tenant or client identifier (for multi-tenant scenarios)
    /// </summary>
    public string TenantId { get; set; }

    /// <summary>
    /// Additional headers from the request
    /// </summary>
    public Dictionary<string, string> Headers { get; set; }

    /// <summary>
    /// Custom metadata passed with the request
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; }

    /// <summary>
    /// The raw decrypted JSON payload (for debugging/logging)
    /// </summary>
    public string RawPayload { get; set; }

    public WebhookContext()
    {
      RequestId = Guid.NewGuid().ToString("N");
      ReceivedAt = DateTime.UtcNow;
      Headers = new Dictionary<string, string>();
      Metadata = new Dictionary<string, object>();
    }
  }
}
