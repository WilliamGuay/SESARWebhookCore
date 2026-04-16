using System.Collections.Generic;

namespace SESARWebHook.Core.Interfaces
{
  /// <summary>
  /// Factory interface for creating and managing connector instances
  /// </summary>
  public interface IConnectorFactory
  {
    /// <summary>
    /// Creates an instance of a connector by its ID
    /// </summary>
    /// <param name="connectorId">The unique connector identifier</param>
    /// <returns>An instance of the connector, or null if not found</returns>
    IIntegrationConnector CreateConnector(string connectorId);

    /// <summary>
    /// Gets all registered connector IDs
    /// </summary>
    /// <returns>Collection of available connector IDs</returns>
    IEnumerable<string> GetAvailableConnectorIds();

    /// <summary>
    /// Checks if a connector with the given ID is registered
    /// </summary>
    /// <param name="connectorId">The connector ID to check</param>
    /// <returns>True if the connector exists</returns>
    bool ConnectorExists(string connectorId);

    /// <summary>
    /// Registers a connector type with the factory
    /// </summary>
    /// <typeparam name="T">The connector type to register</typeparam>
    void RegisterConnector<T>() where T : IIntegrationConnector, new();
  }
}
