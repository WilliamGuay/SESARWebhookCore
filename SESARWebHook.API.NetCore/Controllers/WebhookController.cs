using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SecureExchangesSDK.Models.Transport;
using SESARWebHook.Core.Models;
using SESARWebHook.Core.Services;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace SESARWebHook.API.Controllers
{
  public class SesarWebHookRequest
  {
    [JsonProperty("args")]
    public SesarWebHook Args { get; set; }
  }

  [ApiController]
  [Route("api/webhook")]
  public class WebhookController : ControllerBase
  {
    /// <summary>
    /// Message unique renvoyé pour tout échec de traitement.
    ///
    /// Volontairement non spécifique : distinguer « déchiffrement échoué » de
    /// « désérialisation échouée » fournirait à un appelant non authentifié un oracle
    /// sur le contenu du payload. Le détail réel est journalisé, corrélé par RequestId.
    /// </summary>
    private const string GenericFailureMessage = "Le traitement de la requête a échoué.";

    private readonly StartupConfig _config;
    private readonly IConfiguration _appConfig;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(StartupConfig config, IConfiguration appConfig, ILogger<WebhookController> logger)
    {
      _config = config;
      _appConfig = appConfig;
      _logger = logger;
    }

    /// <summary>
    /// Identifiant de corrélation de la requête HTTP courante.
    /// Réutilise le TraceIdentifier d'ASP.NET Core pour que les journaux du framework
    /// et les nôtres portent la même valeur.
    /// </summary>
    private string CorrelationId => HttpContext?.TraceIdentifier ?? System.Guid.NewGuid().ToString("N");

    /// <summary>
    /// Point de sortie unique des actions de ce contrôleur.
    ///
    /// En cas d'échec : journalise le détail technique, puis le supprime du résultat et
    /// remplace le message par un libellé générique. Seul le RequestId permet ensuite de
    /// relier la réponse au détail journalisé.
    /// </summary>
    private IActionResult Respond(IntegrationResult result, string requestId)
    {
      result.RequestId = requestId;

      if (result.Success)
      {
        return Ok(result);
      }

      _logger.LogError(
          "Échec du traitement webhook. RequestId={RequestId} ConnectorId={ConnectorId} Message={Message} Détail={ErrorDetails}",
          requestId, result.ConnectorId, result.Message, result.ErrorDetails);

      // Ceinture et bretelles : ErrorDetails porte déjà [JsonIgnore], on le neutralise
      // malgré tout pour qu'aucun autre chemin de sérialisation ne puisse l'exposer.
      result.ErrorDetails = null;
      result.Message = GenericFailureMessage;

      return StatusCode(500, result);
    }

    /// <summary>
    /// Variante de <see cref="Respond"/> pour les échecs construits directement dans le
    /// contrôleur, à partir d'une exception.
    /// </summary>
    private IActionResult RespondWithException(System.Exception ex, string stage, string connectorId, string requestId)
    {
      _logger.LogError(ex,
          "Échec du traitement webhook à l'étape {Stage}. RequestId={RequestId} ConnectorId={ConnectorId}",
          stage, requestId, connectorId);

      var result = IntegrationResult.Fail(GenericFailureMessage, null, connectorId);
      result.RequestId = requestId;

      return StatusCode(500, result);
    }

    [HttpPost("")]
    public async Task<IActionResult> ProcessWebhook([FromBody] SesarWebHookRequest request)
    {
      if (request?.Args == null)
      {
        return BadRequest("Invalid webhook data. Expected format: { \"args\": { \"HashKey\": \"...\", \"CryptedObject\": \"...\" } }");
      }

      var defaultConnector = _appConfig["DefaultConnectorId"] ?? "filesystem";
      return await ProcessWithConnector(request.Args, defaultConnector);
    }

    [HttpPost("rotate")]
    public async Task<IActionResult> ProcessRotateWebhook([FromBody] SesarWebHookRequest request)
    {
      if (request?.Args == null)
      {
        return BadRequest("Invalid webhook data. Expected format: { \"args\": { \"HashKey\": \"...\", \"CryptedObject\": \"...\" } }");
      }

      var defaultConnector = _appConfig["DefaultConnectorId"] ?? "filesystem";
      return await ProcessWithConnector(request.Args, defaultConnector, true);
    }

    [HttpPost("handler/{handlerId}")]
    public async Task<IActionResult> ProcessWebhookWithHandler(
        string handlerId,
        [FromBody] SesarWebHookRequest request)
    {
      if (request?.Args == null)
      {
        return BadRequest("Invalid webhook data. Expected format: { \"args\": { \"HashKey\": \"...\", \"CryptedObject\": \"...\" } }");
      }

      if (string.IsNullOrEmpty(handlerId))
      {
        return BadRequest("Handler ID is required");
      }

      var processor = _config.WebhookProcessor;
      if (processor == null)
      {
        return StatusCode(500, new { Error = "Webhook processor not initialized. Check encryption key configuration." });
      }

      var handlerRegistry = _config.HandlerRegistry;
      if (handlerRegistry == null || !handlerRegistry.HandlerExists(handlerId))
      {
        return NotFound();
      }

      var genericConnector = _config.GenericConnector;
      if (genericConnector == null)
      {
        return StatusCode(500, new { Error = "Generic connector not initialized." });
      }

      var requestId = CorrelationId;

      if (!processor.ValidateAuthentication(request.Args.HashKey))
      {
        _logger.LogWarning("Authentification webhook refusée. RequestId={RequestId} HandlerId={HandlerId}",
            requestId, handlerId);

        var authResult = IntegrationResult.Fail("Authentication failed", null, handlerId);
        authResult.RequestId = requestId;
        return StatusCode((int)HttpStatusCode.Unauthorized, authResult);
      }

      string jsonPayload;
      try
      {
        jsonPayload = processor.DecryptPayload(request.Args.CryptedObject);
      }
      catch (System.Exception ex)
      {
        return RespondWithException(ex, "déchiffrement", handlerId, requestId);
      }

      SecureExchangesSDK.Models.Messenging.StoreManifest manifest;
      try
      {
        manifest = SecureExchangesSDK.Helpers.SerializationHelper.DeserializeFromJson<SecureExchangesSDK.Models.Messenging.StoreManifest>(jsonPayload);
      }
      catch (System.Exception ex)
      {
        return RespondWithException(ex, "désérialisation", handlerId, requestId);
      }

      var context = new WebhookContext
      {
        ConnectorId = handlerId,
        RequestId = requestId,
        Metadata = new Dictionary<string, object>
                {
                    { "HandlerId", handlerId }
                },
        RawPayload = jsonPayload
      };

      var result = await genericConnector.ProcessManifestAsync(manifest, context);

      return Respond(result, requestId);
    }

    [HttpPost("{connectorId}")]
    public async Task<IActionResult> ProcessWebhookWithConnector(
        string connectorId,
        [FromBody] SesarWebHookRequest request)
    {
      if (request?.Args == null)
      {
        return BadRequest("Invalid webhook data. Expected format: { \"args\": { \"HashKey\": \"...\", \"CryptedObject\": \"...\" } }");
      }

      if (string.IsNullOrEmpty(connectorId))
      {
        return BadRequest("Connector ID is required");
      }

      return await ProcessWithConnector(request.Args, connectorId);
    }

    [HttpPost("multi")]
    public async Task<IActionResult> ProcessWebhookMultiple(
        [FromQuery] string connectors,
        [FromBody] SesarWebHookRequest request)
    {
      if (request?.Args == null)
      {
        return BadRequest("Invalid webhook data. Expected format: { \"args\": { \"HashKey\": \"...\", \"CryptedObject\": \"...\" } }");
      }

      if (string.IsNullOrEmpty(connectors))
      {
        return BadRequest("At least one connector ID is required");
      }

      var processor = _config.WebhookProcessor;
      if (processor == null)
      {
        return StatusCode(500, new { Error = "Webhook processor not initialized. Check encryption key configuration." });
      }

      var requestId = CorrelationId;
      var connectorIds = connectors.Split(',');
      var results = await processor.ProcessWebhookWithMultipleConnectorsAsync(request.Args, requestId, connectorIds);

      return Ok(new
      {
        Success = true,
        RequestId = requestId,
        Results = SanitizeResults(results, requestId)
      });
    }

    /// <summary>
    /// Applique le même traitement que <see cref="Respond"/> à un lot de résultats :
    /// journalise les détails techniques puis les retire des objets renvoyés.
    /// </summary>
    private IntegrationResult[] SanitizeResults(IntegrationResult[] results, string requestId)
    {
      foreach (var result in results)
      {
        if (result == null) continue;

        result.RequestId = requestId;

        if (!result.Success)
        {
          _logger.LogError(
              "Échec du traitement webhook. RequestId={RequestId} ConnectorId={ConnectorId} Message={Message} Détail={ErrorDetails}",
              requestId, result.ConnectorId, result.Message, result.ErrorDetails);

          result.ErrorDetails = null;
          result.Message = GenericFailureMessage;
        }
      }

      return results;
    }

    [HttpPost("handler/multi")]
    public async Task<IActionResult> ProcessWebhookWithMultipleHandlers(
        [FromQuery] string handlers,
        [FromBody] SesarWebHookRequest request)
    {
      if (request?.Args == null)
      {
        return BadRequest("Invalid webhook data. Expected format: { \"args\": { \"HashKey\": \"...\", \"CryptedObject\": \"...\" } }");
      }

      if (string.IsNullOrEmpty(handlers))
      {
        return BadRequest("At least one handler ID is required. Usage: /api/webhook/handler/multi?handlers=handler1,handler2");
      }

      var processor = _config.WebhookProcessor;
      if (processor == null)
      {
        return StatusCode(500, new { Error = "Webhook processor not initialized. Check encryption key configuration." });
      }

      var handlerRegistry = _config.HandlerRegistry;
      var genericConnector = _config.GenericConnector;
      if (handlerRegistry == null || genericConnector == null)
      {
        return StatusCode(500, new { Error = "Handler system not initialized." });
      }

      var requestId = CorrelationId;

      if (!processor.ValidateAuthentication(request.Args.HashKey))
      {
        _logger.LogWarning("Authentification webhook refusée. RequestId={RequestId} HandlerId={HandlerId}",
            requestId, "multi-handler");

        var authResult = IntegrationResult.Fail("Authentication failed", null, "multi-handler");
        authResult.RequestId = requestId;
        return StatusCode((int)HttpStatusCode.Unauthorized, authResult);
      }

      string jsonPayload;
      try
      {
        jsonPayload = processor.DecryptPayload(request.Args.CryptedObject);
      }
      catch (System.Exception ex)
      {
        return RespondWithException(ex, "déchiffrement", "multi-handler", requestId);
      }

      SecureExchangesSDK.Models.Messenging.StoreManifest manifest;
      try
      {
        manifest = SecureExchangesSDK.Helpers.SerializationHelper.DeserializeFromJson<SecureExchangesSDK.Models.Messenging.StoreManifest>(jsonPayload);
      }
      catch (System.Exception ex)
      {
        return RespondWithException(ex, "désérialisation", "multi-handler", requestId);
      }

      var handlerIds = handlers.Split(',');
      var tasks = new List<Task<IntegrationResult>>();
      var unknownHandlers = new List<string>();

      foreach (var handlerId in handlerIds)
      {
        var id = handlerId.Trim();
        if (string.IsNullOrEmpty(id)) continue;

        if (!handlerRegistry.HandlerExists(id))
        {
          unknownHandlers.Add(id);
          continue;
        }

        var context = new WebhookContext
        {
          ConnectorId = id,
          RequestId = requestId,
          Metadata = new Dictionary<string, object>
                    {
                        { "HandlerId", id }
                    },
          RawPayload = jsonPayload
        };

        tasks.Add(genericConnector.ProcessManifestAsync(manifest, context));
      }

      var results = new List<object>();

      foreach (var unknown in unknownHandlers)
      {
        // « Handler introuvable » n'est pas une information sensible : l'inventaire est
        // de toute façon fourni par /api/handlers. On garde donc le message explicite,
        // mais sans énumérer les handlers disponibles.
        var notFound = IntegrationResult.Fail("Handler not found", null, unknown);
        notFound.RequestId = requestId;
        results.Add(notFound);
      }

      if (tasks.Count > 0)
      {
        var handlerResults = await Task.WhenAll(tasks);
        foreach (var result in SanitizeResults(handlerResults, requestId))
        {
          results.Add(result);
        }
      }

      return Ok(new
      {
        Success = unknownHandlers.Count == 0 && tasks.Count > 0,
        RequestId = requestId,
        TotalHandlers = handlerIds.Length,
        Processed = tasks.Count,
        Failed = unknownHandlers.Count,
        Results = results
      });
    }

    private async Task<IActionResult> ProcessWithConnector(SesarWebHook webhookData, string connectorId, bool isRotate = false)
    {
      var processor = _config.WebhookProcessor;
      if (processor == null)
      {
        return StatusCode(500, new { Error = "Webhook processor not initialized. Check encryption key configuration." });
      }

      var settings = _config.GetConnectorSettings(connectorId);
      var connector = _config.ConnectorRegistry.GetOrCreateConnector(connectorId, settings);

      if (connector == null)
      {
        return NotFound();
      }

      var requestId = CorrelationId;
      var result = await processor.ProcessWebhookAsync(webhookData, connectorId, isRotate, requestId);

      return Respond(result, requestId);
    }
  }
}
