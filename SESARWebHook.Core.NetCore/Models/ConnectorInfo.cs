using System.Collections.Generic;

namespace SESARWebHook.Core.Models
{
  /// <summary>
  /// Information about a registered connector
  /// </summary>
  public class ConnectorInfo
  {
    /// <summary>
    /// Unique identifier for the connector
    /// </summary>
    public string ConnectorId { get; set; }

    /// <summary>
    /// Human-readable display name
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// Description of what the connector does
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Version of the connector
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// List of required configuration keys
    /// </summary>
    public IEnumerable<string> RequiredConfigurationKeys { get; set; }

    /// <summary>
    /// Indicates if the connector is currently enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Indicates if the connector has valid configuration
    /// </summary>
    public bool IsConfigured { get; set; }
  }
}
