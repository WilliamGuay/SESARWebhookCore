using SESARWebHook.Core.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SESARWebHook.Core.Services
{
  /// <summary>
  /// Registry for dynamically loading and managing IWebhookHandler implementations.
  ///
  /// Handlers are loaded from:
  /// 1. Manually registered types (via RegisterHandler)
  /// 2. DLLs dropped in the /Handlers/ folder (scanned at startup)
  ///
  /// CONFIGURATION:
  /// Set "HandlersPath" in Web.config to customize the handlers folder location.
  /// Default: {AppDomain.BaseDirectory}/Handlers/
  /// </summary>
  public class HandlerRegistry
  {
    private readonly ConcurrentDictionary<string, Type> _handlerTypes;
    private readonly ConcurrentDictionary<string, IWebhookHandler> _handlerInstances;
    private static readonly object _scanLock = new object();
    private static bool _scanned = false;

    public HandlerRegistry()
    {
      _handlerTypes = new ConcurrentDictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
      _handlerInstances = new ConcurrentDictionary<string, IWebhookHandler>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Registers a handler type manually
    /// </summary>
    public void RegisterHandler<T>() where T : IWebhookHandler, new()
    {
      var instance = new T();
      _handlerTypes[instance.HandlerId] = typeof(T);
    }

    /// <summary>
    /// Registers a handler type with a specific ID
    /// </summary>
    public void RegisterHandler(string handlerId, Type handlerType)
    {
      if (!typeof(IWebhookHandler).IsAssignableFrom(handlerType))
      {
        throw new ArgumentException($"Type {handlerType.Name} must implement IWebhookHandler");
      }
      _handlerTypes[handlerId] = handlerType;
    }

    /// <summary>
    /// Scans the handlers folder for DLLs containing IWebhookHandler implementations
    /// </summary>
    /// <param name="handlersPath">Path to scan. If null, uses default /Handlers/ folder</param>
    public void ScanForHandlers(string handlersPath = null)
    {
      lock (_scanLock)
      {
        if (_scanned && handlersPath == null)
          return;

        var path = handlersPath ?? GetDefaultHandlersPath();

        if (!Directory.Exists(path))
        {
          // Create the handlers folder if it doesn't exist
          try
          {
            Directory.CreateDirectory(path);
          }
          catch
          {
            // Ignore - folder creation is optional
          }
          return;
        }

        var dllFiles = Directory.GetFiles(path, "*.dll", SearchOption.TopDirectoryOnly);

        foreach (var dllFile in dllFiles)
        {
          try
          {
            LoadHandlersFromAssembly(dllFile);
          }
          catch (Exception ex)
          {
            // Log but don't fail - one bad DLL shouldn't break everything
            System.Diagnostics.Trace.TraceWarning(
                $"Failed to load handlers from {Path.GetFileName(dllFile)}: {ex.Message}");
          }
        }

        _scanned = true;
      }
    }

    /// <summary>
    /// Loads handlers from a specific assembly file
    /// </summary>
    private void LoadHandlersFromAssembly(string assemblyPath)
    {
      var assembly = Assembly.LoadFrom(assemblyPath);
      var handlerInterface = typeof(IWebhookHandler);

      var handlerTypes = assembly.GetTypes()
          .Where(t => handlerInterface.IsAssignableFrom(t)
                      && !t.IsInterface
                      && !t.IsAbstract
                      && t.GetConstructor(Type.EmptyTypes) != null);

      foreach (var type in handlerTypes)
      {
        try
        {
          var instance = (IWebhookHandler)Activator.CreateInstance(type);
          _handlerTypes[instance.HandlerId] = type;

          System.Diagnostics.Trace.TraceInformation(
              $"Loaded handler '{instance.HandlerId}' ({instance.DisplayName}) from {Path.GetFileName(assemblyPath)}");
        }
        catch (Exception ex)
        {
          System.Diagnostics.Trace.TraceWarning(
              $"Failed to instantiate handler {type.Name}: {ex.Message}");
        }
      }
    }

    /// <summary>
    /// Gets the default handlers path
    /// </summary>
    private string GetDefaultHandlersPath()
    {
      var basePath = AppDomain.CurrentDomain.BaseDirectory;
      return Path.Combine(basePath, "Handlers");
    }

    /// <summary>
    /// Creates a new instance of a handler
    /// </summary>
    public IWebhookHandler CreateHandler(string handlerId)
    {
      // Ensure handlers are scanned
      ScanForHandlers();

      if (!_handlerTypes.TryGetValue(handlerId, out var handlerType))
      {
        return null;
      }

      return (IWebhookHandler)Activator.CreateInstance(handlerType);
    }

    /// <summary>
    /// Gets or creates a singleton instance of a handler
    /// </summary>
    public IWebhookHandler GetOrCreateHandler(string handlerId, Dictionary<string, string> settings = null)
    {
      // Ensure handlers are scanned
      ScanForHandlers();

      return _handlerInstances.GetOrAdd(handlerId, id =>
      {
        var handler = CreateHandler(id);
        if (handler != null && settings != null)
        {
          handler.Initialize(settings);
        }
        return handler;
      });
    }

    /// <summary>
    /// Gets all registered handler IDs
    /// </summary>
    public IEnumerable<string> GetAvailableHandlerIds()
    {
      ScanForHandlers();
      return _handlerTypes.Keys.ToList();
    }

    /// <summary>
    /// Checks if a handler exists
    /// </summary>
    public bool HandlerExists(string handlerId)
    {
      ScanForHandlers();
      return _handlerTypes.ContainsKey(handlerId);
    }

    /// <summary>
    /// Gets information about all registered handlers
    /// </summary>
    public IEnumerable<HandlerInfo> GetHandlerInfos()
    {
      ScanForHandlers();

      foreach (var kvp in _handlerTypes)
      {
        IWebhookHandler instance = null;
        try
        {
          instance = CreateHandler(kvp.Key);
        }
        catch
        {
          continue;
        }

        if (instance != null)
        {
          yield return new HandlerInfo
          {
            HandlerId = instance.HandlerId,
            DisplayName = instance.DisplayName,
            Description = instance.Description,
            Version = instance.Version
          };
        }
      }
    }

    /// <summary>
    /// Clears a cached handler instance
    /// </summary>
    public void ClearHandlerInstance(string handlerId)
    {
      _handlerInstances.TryRemove(handlerId, out _);
    }

    /// <summary>
    /// Clears all cached handler instances
    /// </summary>
    public void ClearAllInstances()
    {
      _handlerInstances.Clear();
    }

    /// <summary>
    /// Forces a rescan of the handlers folder
    /// </summary>
    public void Rescan()
    {
      lock (_scanLock)
      {
        _scanned = false;
      }
      ScanForHandlers();
    }
  }

  /// <summary>
  /// Information about a registered handler
  /// </summary>
  public class HandlerInfo
  {
    public string HandlerId { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string Version { get; set; }
  }
}
