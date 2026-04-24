using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SESARWebHook.API.Controllers
{
  [ApiController]
  [Route("api/debug")]
  public class DebugController : ControllerBase
  {
    private readonly StartupConfig _config;
    private readonly IConfiguration _appConfig;

    public DebugController(StartupConfig config, IConfiguration appConfig)
    {
      _config = config;
      _appConfig = appConfig;
    }

    private bool IsDebugEnabled
    {
      get
      {
        var setting = _appConfig["EnableDebugEndpoints"];
        return !string.IsNullOrEmpty(setting) &&
               setting.Equals("true", StringComparison.OrdinalIgnoreCase);
      }
    }

    [HttpPost("connector/{connectorId}")]
    public async Task<IActionResult> DebugConnector(
        string connectorId,
        [FromBody] StoreManifest manifest)
    {
      if (!IsDebugEnabled)
      {
        return BadRequest("Debug endpoints are disabled. Set EnableDebugEndpoints=true in appsettings.json.");
      }

      if (manifest == null)
      {
        return BadRequest("Request body must be a valid StoreManifest JSON.");
      }

      try
      {
        var settings = _config.GetConnectorSettings(connectorId);
        var connector = _config.ConnectorRegistry.GetOrCreateConnector(connectorId, settings);

        if (connector == null)
        {
          return NotFound();
        }

        var context = new WebhookContext
        {
          ConnectorId = connectorId,
          RawPayload = JsonConvert.SerializeObject(manifest),
          Metadata = new Dictionary<string, object>
                    {
                        { "DebugMode", true }
                    }
        };

        var result = await connector.ProcessManifestAsync(manifest, context);

        return Ok(result);
      }
      catch (Exception ex)
      {
        return StatusCode(500, new { Error = ex.Message, Details = ex.ToString() });
      }
    }

    [HttpPost("handler/{handlerId}")]
    public async Task<IActionResult> DebugHandler(
        string handlerId,
        [FromBody] StoreManifest manifest)
    {
      if (!IsDebugEnabled)
      {
        return BadRequest("Debug endpoints are disabled. Set EnableDebugEndpoints=true in appsettings.json.");
      }

      if (manifest == null)
      {
        return BadRequest("Request body must be a valid StoreManifest JSON.");
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

      try
      {
        var context = new WebhookContext
        {
          ConnectorId = handlerId,
          RawPayload = JsonConvert.SerializeObject(manifest),
          Metadata = new Dictionary<string, object>
                    {
                        { "HandlerId", handlerId },
                        { "DebugMode", true }
                    }
        };

        var result = await genericConnector.ProcessManifestAsync(manifest, context);

        return Ok(result);
      }
      catch (Exception ex)
      {
        return StatusCode(500, new { Error = ex.Message, Details = ex.ToString() });
      }
    }

    [HttpGet("sample-manifest")]
    public IActionResult GetSampleManifest()
    {
      if (!IsDebugEnabled)
      {
        return BadRequest("Debug endpoints are disabled. Set EnableDebugEndpoints=true in appsettings.json.");
      }

      var sample = new StoreManifest();

      return Ok(new
      {
        Instructions = new
        {
          Step1 = "Copy the SampleManifest JSON below",
          Step2 = "POST it to /api/debug/connector/filesystem (or your connector ID)",
          Step3 = "Set breakpoints in your ProcessManifestAsync method",
          ContentType = "application/json"
        },
        SampleManifest = sample
      });
    }
  }
}
