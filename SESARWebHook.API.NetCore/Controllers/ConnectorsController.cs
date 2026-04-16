using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace SESARWebHook.API.Controllers
{
  [ApiController]
  [Route("api/connectors")]
  public class ConnectorsController : ControllerBase
  {
    private readonly StartupConfig _config;
    private readonly IConfiguration _appConfig;

    public ConnectorsController(StartupConfig config, IConfiguration appConfig)
    {
      _config = config;
      _appConfig = appConfig;
    }

    [HttpGet("")]
    public IActionResult GetConnectors()
    {
      var registry = _config.ConnectorRegistry;
      var connectors = registry.GetConnectorInfos().ToList();

      foreach (var connector in connectors)
      {
        var enabledSetting = _appConfig[$"Connector:{connector.ConnectorId}:Enabled"];
        connector.IsEnabled = !string.IsNullOrEmpty(enabledSetting) &&
                             bool.TryParse(enabledSetting, out var enabled) && enabled;
      }

      return Ok(connectors);
    }

    [HttpGet("{connectorId}")]
    public IActionResult GetConnector(string connectorId)
    {
      var registry = _config.ConnectorRegistry;

      if (!registry.ConnectorExists(connectorId))
      {
        return NotFound();
      }

      var connector = registry.CreateConnector(connectorId);
      var enabledSetting = _appConfig[$"Connector:{connectorId}:Enabled"];
      var isEnabled = !string.IsNullOrEmpty(enabledSetting) &&
                     bool.TryParse(enabledSetting, out var enabled) && enabled;

      return Ok(new
      {
        connector.ConnectorId,
        connector.DisplayName,
        connector.Description,
        connector.Version,
        connector.RequiredConfigurationKeys,
        IsEnabled = isEnabled
      });
    }

    [HttpPost("{connectorId}/test")]
    public async Task<IActionResult> TestConnector(string connectorId)
    {
      var registry = _config.ConnectorRegistry;

      if (!registry.ConnectorExists(connectorId))
      {
        return NotFound();
      }

      var settings = _config.GetConnectorSettings(connectorId);
      var connector = registry.GetOrCreateConnector(connectorId, settings);

      try
      {
        var isValid = await connector.ValidateConfigurationAsync(settings);
        if (!isValid)
        {
          return Ok(new
          {
            Success = false,
            Message = "Configuration validation failed",
            ConnectorId = connectorId
          });
        }

        var connectionOk = await connector.TestConnectionAsync();
        return Ok(new
        {
          Success = connectionOk,
          Message = connectionOk ? "Connection successful" : "Connection failed",
          ConnectorId = connectorId
        });
      }
      catch (System.Exception ex)
      {
        return Ok(new
        {
          Success = false,
          Message = "Connection test failed",
          Error = ex.Message,
          ConnectorId = connectorId
        });
      }
    }
  }
}
