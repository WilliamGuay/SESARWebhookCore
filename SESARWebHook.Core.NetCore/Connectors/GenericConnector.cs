using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core.Configuration;
using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Models;
using SESARWebHook.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SESARWebHook.Core.Connectors
{
  /// <summary>
  /// Connecteur générique qui délègue le traitement aux IWebhookHandler.
  ///
  /// Ce connecteur :
  /// - Charge les handlers depuis le dossier /Handlers/
  /// - Route les requêtes vers le bon handler
  /// - Laisse le handler faire sa logique d'affaire
  ///
  /// Le client implémente IWebhookHandler/WebhookHandlerBase et fait ce qu'il veut :
  /// - Extraction des données du manifest
  /// - Authentification (avec nos helpers OAuth2/ApiKey)
  /// - Appels HTTP vers son système
  /// - Transformation/mapping des données
  ///
  /// ROUTAGE :
  /// - /api/webhook/handler/{handler-id} → Handler spécifique
  /// - Configuration DefaultHandlerId dans Web.config
  /// </summary>
  public class GenericConnector : IIntegrationConnector
  {
    private readonly HandlerRegistry _handlerRegistry;
    private Dictionary<string, string> _settings;

    public GenericConnector()
    {
      _handlerRegistry = new HandlerRegistry();
    }

    public GenericConnector(HandlerRegistry handlerRegistry)
    {
      _handlerRegistry = handlerRegistry ?? new HandlerRegistry();
    }

    #region IIntegrationConnector Implementation

    public string ConnectorId => "generic";
    public string DisplayName => "Generic Handler Connector";
    public string Description => "Routes requests to client-implemented IWebhookHandler instances";
    public string Version => "1.0.0";

    public IEnumerable<string> RequiredConfigurationKeys => Enumerable.Empty<string>();

    public void Initialize(Dictionary<string, string> settings)
    {
      _settings = settings ?? new Dictionary<string, string>();

      // Scanner le dossier /Handlers/ pour charger les DLLs
      var handlersPath = WebHookConfigHelper.HandlersPath;
      _handlerRegistry.ScanForHandlers(handlersPath);
    }

    public Task<bool> ValidateConfigurationAsync(Dictionary<string, string> settings)
    {
      return Task.FromResult(true);
    }

    public Task<bool> TestConnectionAsync()
    {
      // Vérifier qu'au moins un handler est disponible
      var handlers = _handlerRegistry.GetAvailableHandlerIds().ToList();
      return Task.FromResult(handlers.Count > 0);
    }

    public async Task<IntegrationResult> ProcessManifestAsync(StoreManifest manifest, WebhookContext context)
    {
      // 1. Déterminer quel handler utiliser
      var handlerId = GetHandlerIdFromContext(context);

      if (string.IsNullOrEmpty(handlerId))
      {
        return IntegrationResult.Fail(
            "No handler specified",
            "Specify a handler using /api/webhook/handler/{handler-id}",
            ConnectorId);
      }

      // 2. Vérifier que le handler existe
      if (!_handlerRegistry.HandlerExists(handlerId))
      {
        var availableHandlers = string.Join(", ", _handlerRegistry.GetAvailableHandlerIds());
        return IntegrationResult.Fail(
            $"Handler '{handlerId}' not found",
            $"Available handlers: {(string.IsNullOrEmpty(availableHandlers) ? "(none)" : availableHandlers)}",
            ConnectorId);
      }

      // 3. Charger les settings du handler
      var handlerSettings = WebHookConfigHelper.GetHandlerSettings(handlerId);

      // 4. Obtenir l'instance du handler
      var handler = _handlerRegistry.GetOrCreateHandler(handlerId, handlerSettings);

      if (handler == null)
      {
        return IntegrationResult.Fail(
            $"Failed to create handler '{handlerId}'",
            "Handler instantiation failed",
            ConnectorId);
      }

      try
      {
        // 5. Appeler ProcessAsync - le handler fait ce qu'il veut
        var result = await handler.ProcessAsync(manifest, context);

        // 6. S'assurer que le ConnectorId est set
        if (string.IsNullOrEmpty(result.ConnectorId))
        {
          result.ConnectorId = handlerId;
        }

        // Ajouter des métadonnées
        if (result.Metadata == null)
        {
          result.Metadata = new Dictionary<string, object>();
        }
        result.Metadata["HandlerId"] = handlerId;
        result.Metadata["HandlerVersion"] = handler.Version;

        return result;
      }
      catch (Exception ex)
      {
        return IntegrationResult.Fail(
            $"Handler '{handlerId}' threw an exception: {ex.Message}",
            ex.ToString(),
            handlerId);
      }
    }

    #endregion

    #region Helper Methods

    private string GetHandlerIdFromContext(WebhookContext context)
    {
      // 1. Check metadata (set by routing)
      if (context.Metadata?.TryGetValue("HandlerId", out var handlerIdObj) == true)
      {
        return handlerIdObj?.ToString();
      }

      // 2. Check ConnectorId (might be the handler ID)
      if (!string.IsNullOrEmpty(context.ConnectorId) &&
          _handlerRegistry.HandlerExists(context.ConnectorId))
      {
        return context.ConnectorId;
      }

      // 3. Check settings for default handler
      if (_settings?.TryGetValue("DefaultHandlerId", out var defaultHandler) == true)
      {
        return defaultHandler;
      }

      return null;
    }

    #endregion

    #region Handler Management

    /// <summary>
    /// Obtient le registry des handlers
    /// </summary>
    public HandlerRegistry GetHandlerRegistry() => _handlerRegistry;

    /// <summary>
    /// Liste les handlers disponibles
    /// </summary>
    public IEnumerable<string> GetAvailableHandlerIds() => _handlerRegistry.GetAvailableHandlerIds();

    /// <summary>
    /// Obtient les infos sur les handlers
    /// </summary>
    public IEnumerable<HandlerInfo> GetHandlerInfos() => _handlerRegistry.GetHandlerInfos();

    #endregion
  }
}
