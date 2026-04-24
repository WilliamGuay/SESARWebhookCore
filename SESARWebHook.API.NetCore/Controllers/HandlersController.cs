using Microsoft.AspNetCore.Mvc;
using SESARWebHook.Core.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace SESARWebHook.API.Controllers
{
  [ApiController]
  [Route("api/handlers")]
  public class HandlersController : ControllerBase
  {
    private readonly StartupConfig _config;

    public HandlersController(StartupConfig config)
    {
      _config = config;
    }

    [HttpGet("")]
    public IActionResult GetHandlers()
    {
      var registry = _config.HandlerRegistry;
      if (registry == null)
      {
        return Ok(new { Handlers = new object[0], Message = "Handler registry not initialized" });
      }

      var handlers = registry.GetHandlerInfos().Select(h => new
      {
        h.HandlerId,
        h.DisplayName,
        h.Description,
        h.Version,
        IsEnabled = WebHookConfigHelper.IsHandlerEnabled(h.HandlerId),
        WebhookUrl = $"/api/webhook/handler/{h.HandlerId}"
      }).ToList();

      return Ok(handlers);
    }

    [HttpGet("{handlerId}")]
    public IActionResult GetHandler(string handlerId)
    {
      var registry = _config.HandlerRegistry;
      if (registry == null || !registry.HandlerExists(handlerId))
      {
        return NotFound();
      }

      var handler = registry.CreateHandler(handlerId);
      if (handler == null)
      {
        return NotFound();
      }

      return Ok(new
      {
        handler.HandlerId,
        handler.DisplayName,
        handler.Description,
        handler.Version,
        IsEnabled = WebHookConfigHelper.IsHandlerEnabled(handlerId),
        WebhookUrl = $"/api/webhook/handler/{handlerId}"
      });
    }

    [HttpPost("{handlerId}/test")]
    public async Task<IActionResult> TestHandler(string handlerId)
    {
      var registry = _config.HandlerRegistry;
      if (registry == null || !registry.HandlerExists(handlerId))
      {
        return NotFound();
      }

      var settings = WebHookConfigHelper.GetHandlerSettings(handlerId);
      var handler = registry.GetOrCreateHandler(handlerId, settings);

      if (handler == null)
      {
        return Ok(new
        {
          Success = false,
          Message = "Failed to create handler instance",
          HandlerId = handlerId
        });
      }

      try
      {
        var isValid = await handler.ValidateConfigurationAsync(settings);
        if (!isValid)
        {
          return Ok(new
          {
            Success = false,
            Message = "Configuration validation failed",
            HandlerId = handlerId
          });
        }

        var connectionOk = await handler.TestConnectionAsync();
        return Ok(new
        {
          Success = connectionOk,
          Message = connectionOk ? "Connection successful" : "Connection failed",
          HandlerId = handlerId
        });
      }
      catch (System.Exception ex)
      {
        return Ok(new
        {
          Success = false,
          Message = "Connection test failed",
          Error = ex.Message,
          HandlerId = handlerId
        });
      }
    }

    [HttpPost("rescan")]
    public IActionResult RescanHandlers()
    {
      var registry = _config.HandlerRegistry;
      if (registry == null)
      {
        return StatusCode(500, new { Error = "Handler registry not initialized" });
      }

      registry.Rescan();

      var handlers = registry.GetAvailableHandlerIds().ToList();
      return Ok(new
      {
        Success = true,
        Message = $"Rescan complete. Found {handlers.Count} handler(s).",
        Handlers = handlers
      });
    }
  }
}
