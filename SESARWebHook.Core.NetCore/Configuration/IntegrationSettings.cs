using System.Collections.Generic;

namespace SESARWebHook.Core.Configuration
{
  /// <summary>
  /// Configuration settings for the webhook integration system
  /// </summary>
  public class IntegrationSettings
  {
    /// <summary>
    /// The encryption key for decrypting SESAR payloads (Base64)
    /// </summary>
    public string EncryptionKey { get; set; }

    /// <summary>
    /// The initialization vector for decryption (Base64)
    /// </summary>
    public string EncryptionIV { get; set; }

    /// <summary>
    /// The default connector to use if none is specified
    /// </summary>
    public string DefaultConnectorId { get; set; }

    /// <summary>
    /// Whether to enable detailed logging
    /// </summary>
    public bool EnableDetailedLogging { get; set; }

    /// <summary>
    /// Path to store log files
    /// </summary>
    public string LogPath { get; set; }

    /// <summary>
    /// Configuration for each connector, keyed by connector ID
    /// </summary>
    public Dictionary<string, ConnectorSettings> Connectors { get; set; }

    public IntegrationSettings()
    {
      Connectors = new Dictionary<string, ConnectorSettings>();
    }
  }

  /// <summary>
  /// Settings for an individual connector
  /// </summary>
  public class ConnectorSettings
  {
    /// <summary>
    /// Whether this connector is enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Connector-specific configuration values
    /// </summary>
    public Dictionary<string, string> Settings { get; set; }

    public ConnectorSettings()
    {
      Settings = new Dictionary<string, string>();
    }
  }
}
