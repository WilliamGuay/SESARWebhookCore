using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SESARWebHook.Core.Interfaces
{
  /// <summary>
  /// Interface that all integration connectors must implement.
  /// Each connector handles synchronization with a specific external system.
  /// </summary>
  public interface IIntegrationConnector
  {
    /// <summary>
    /// Unique identifier for this connector (e.g., "zoho-crm", "sharepoint", "dynamics-365")
    /// </summary>
    string ConnectorId { get; }

    /// <summary>
    /// Human-readable name for display purposes
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Description of what this connector does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Version of the connector
    /// </summary>
    string Version { get; }

    /// <summary>
    /// List of required configuration keys for this connector
    /// </summary>
    IEnumerable<string> RequiredConfigurationKeys { get; }

    /// <summary>
    /// Validates that the provided configuration is valid for this connector
    /// </summary>
    /// <param name="settings">Configuration settings to validate</param>
    /// <returns>True if configuration is valid</returns>
    Task<bool> ValidateConfigurationAsync(Dictionary<string, string> settings);

    /// <summary>
    /// Processes a StoreManifest from SESAR and sends it to the external system
    /// </summary>
    /// <param name="manifest">The decrypted store manifest from SESAR</param>
    /// <param name="context">Additional context about the webhook request</param>
    /// <returns>Result of the integration operation</returns>
    Task<IntegrationResult> ProcessManifestAsync(StoreManifest manifest, WebhookContext context);

    /// <summary>
    /// Tests the connection to the external system
    /// </summary>
    /// <returns>True if connection is successful</returns>
    Task<bool> TestConnectionAsync();

    /// <summary>
    /// Initializes the connector with its configuration
    /// </summary>
    /// <param name="settings">Configuration settings for the connector</param>
    void Initialize(Dictionary<string, string> settings);
  }
}
