using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SESARWebHook.Tests.Fakes
{
  /// <summary>
  /// Fake connector for unit testing. Tracks calls and allows configurable behavior.
  /// </summary>
  public class FakeConnector : IIntegrationConnector
  {
    public string ConnectorId { get; set; } = "fake-connector";
    public string DisplayName { get; set; } = "Fake Connector";
    public string Description { get; set; } = "A fake connector for testing";
    public string Version { get; set; } = "1.0.0";
    public IEnumerable<string> RequiredConfigurationKeys { get; set; } = new List<string>();

    // Tracking
    public bool InitializeCalled { get; private set; }
    public Dictionary<string, string> LastSettings { get; private set; }
    public int ProcessCallCount { get; private set; }
    public StoreManifest LastManifest { get; private set; }
    public WebhookContext LastContext { get; private set; }

    // Configurable behavior
    public bool ShouldSucceed { get; set; } = true;
    public bool ValidateConfigResult { get; set; } = true;
    public bool TestConnectionResult { get; set; } = true;
    public string ResultMessage { get; set; } = "Processed by FakeConnector";

    public void Initialize(Dictionary<string, string> settings)
    {
      InitializeCalled = true;
      LastSettings = settings;
    }

    public Task<bool> ValidateConfigurationAsync(Dictionary<string, string> settings)
    {
      return Task.FromResult(ValidateConfigResult);
    }

    public Task<IntegrationResult> ProcessManifestAsync(StoreManifest manifest, WebhookContext context)
    {
      ProcessCallCount++;
      LastManifest = manifest;
      LastContext = context;

      if (ShouldSucceed)
      {
        return Task.FromResult(IntegrationResult.Ok(ResultMessage, ConnectorId));
      }
      else
      {
        return Task.FromResult(IntegrationResult.Fail(ResultMessage, "Test failure", ConnectorId));
      }
    }

    public Task<bool> TestConnectionAsync()
    {
      return Task.FromResult(TestConnectionResult);
    }
  }

  /// <summary>
  /// Second fake connector to test multi-connector scenarios
  /// </summary>
  public class FakeConnector2 : FakeConnector
  {
    public FakeConnector2()
    {
      ConnectorId = "fake-connector-2";
      DisplayName = "Fake Connector 2";
      Description = "Second fake connector for testing";
    }
  }
}
