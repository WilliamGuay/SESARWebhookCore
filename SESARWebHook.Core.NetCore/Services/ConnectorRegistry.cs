using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SESARWebHook.Core.Services
{
  /// <summary>
  /// Registry for managing integration connectors
  /// </summary>
  public class ConnectorRegistry : IConnectorFactory
  {
    private readonly ConcurrentDictionary<string, Type> _connectorTypes;
    private readonly ConcurrentDictionary<string, IIntegrationConnector> _connectorInstances;

    public ConnectorRegistry()
    {
      _connectorTypes = new ConcurrentDictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
      _connectorInstances = new ConcurrentDictionary<string, IIntegrationConnector>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Registers a connector type with the registry
    /// </summary>
    public void RegisterConnector<T>() where T : IIntegrationConnector, new()
    {
      var instance = new T();
      _connectorTypes[instance.ConnectorId] = typeof(T);
    }

    /// <summary>
    /// Registers a connector type with a specific ID
    /// </summary>
    public void RegisterConnector(string connectorId, Type connectorType)
    {
      if (!typeof(IIntegrationConnector).IsAssignableFrom(connectorType))
      {
        throw new ArgumentException($"Type {connectorType.Name} must implement IIntegrationConnector");
      }
      _connectorTypes[connectorId] = connectorType;
    }

    /// <summary>
    /// Creates a new instance of a connector
    /// </summary>
    public IIntegrationConnector CreateConnector(string connectorId)
    {
      if (!_connectorTypes.TryGetValue(connectorId, out var connectorType))
      {
        return null;
      }

      return (IIntegrationConnector)Activator.CreateInstance(connectorType);
    }

    /// <summary>
    /// Gets or creates a singleton instance of a connector
    /// </summary>
    public IIntegrationConnector GetOrCreateConnector(string connectorId, Dictionary<string, string> settings = null)
    {
      return _connectorInstances.GetOrAdd(connectorId, id =>
      {
        var connector = CreateConnector(id);
        if (connector != null && settings != null)
        {
          connector.Initialize(settings);
        }
        return connector;
      });
    }

    /// <summary>
    /// Gets all registered connector IDs
    /// </summary>
    public IEnumerable<string> GetAvailableConnectorIds()
    {
      return _connectorTypes.Keys.ToList();
    }

    /// <summary>
    /// Checks if a connector exists
    /// </summary>
    public bool ConnectorExists(string connectorId)
    {
      return _connectorTypes.ContainsKey(connectorId);
    }

    /// <summary>
    /// Gets information about all registered connectors
    /// </summary>
    public IEnumerable<ConnectorInfo> GetConnectorInfos()
    {
      foreach (var kvp in _connectorTypes)
      {
        var instance = CreateConnector(kvp.Key);
        if (instance != null)
        {
          yield return new ConnectorInfo
          {
            ConnectorId = instance.ConnectorId,
            DisplayName = instance.DisplayName,
            Description = instance.Description,
            Version = instance.Version,
            RequiredConfigurationKeys = instance.RequiredConfigurationKeys
          };
        }
      }
    }

    /// <summary>
    /// Pre-caches a connector instance (useful for connectors initialized externally)
    /// </summary>
    public void PreCacheConnectorInstance(string connectorId, IIntegrationConnector instance)
    {
      _connectorInstances[connectorId] = instance;
    }

    /// <summary>
    /// Clears a cached connector instance
    /// </summary>
    public void ClearConnectorInstance(string connectorId)
    {
      _connectorInstances.TryRemove(connectorId, out _);
    }

    /// <summary>
    /// Clears all cached connector instances
    /// </summary>
    public void ClearAllInstances()
    {
      _connectorInstances.Clear();
    }
  }
}
