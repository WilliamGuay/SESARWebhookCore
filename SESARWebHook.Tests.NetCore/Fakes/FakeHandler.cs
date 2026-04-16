using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core;
using SESARWebHook.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SESARWebHook.Tests.Fakes
{
  /// <summary>
  /// Fake webhook handler for unit testing.
  /// Extends WebhookHandlerBase to also test the base class helpers.
  /// </summary>
  public class FakeHandler : WebhookHandlerBase
  {
    public override string HandlerId => "fake-handler";
    public override string DisplayName => "Fake Handler";
    public override string Description => "A fake handler for testing";
    public override string Version => "1.0.0-test";

    // Tracking
    public int ProcessCallCount { get; private set; }
    public StoreManifest LastManifest { get; private set; }
    public WebhookContext LastContext { get; private set; }

    // Configurable behavior
    public bool ShouldSucceed { get; set; } = true;
    public string ResultMessage { get; set; } = "Processed by FakeHandler";

    public override Task<IntegrationResult> ProcessAsync(StoreManifest manifest, WebhookContext context)
    {
      ProcessCallCount++;
      LastManifest = manifest;
      LastContext = context;

      if (ShouldSucceed)
      {
        return Task.FromResult(IntegrationResult.Ok(ResultMessage, HandlerId));
      }
      else
      {
        return Task.FromResult(IntegrationResult.Fail(ResultMessage, "Test failure", HandlerId));
      }
    }

    // Expose protected helpers for testing
    public string TestGetSetting(string key) => GetSetting(key);
    public string TestGetRequiredSetting(string key) => GetRequiredSetting(key);
    public string TestGetSettingDefault(string key, string defaultValue) => GetSetting(key, defaultValue);
    public bool TestGetSettingBool(string key, bool defaultValue = false) => GetSettingBool(key, defaultValue);
    public int TestGetSettingInt(string key, int defaultValue = 0) => GetSettingInt(key, defaultValue);
    public string TestDecodeBase64(string base64) => DecodeBase64(base64);

    /// <summary>
    /// Expose the internal Settings dictionary for verification
    /// </summary>
    public Dictionary<string, string> ExposedSettings => Settings;
  }

  /// <summary>
  /// Second fake handler for multi-handler testing
  /// </summary>
  public class FakeHandler2 : WebhookHandlerBase
  {
    public override string HandlerId => "fake-handler-2";
    public override string DisplayName => "Fake Handler 2";

    public int ProcessCallCount { get; private set; }
    public bool ShouldSucceed { get; set; } = true;

    public override Task<IntegrationResult> ProcessAsync(StoreManifest manifest, WebhookContext context)
    {
      ProcessCallCount++;

      if (ShouldSucceed)
      {
        return Task.FromResult(IntegrationResult.Ok("Processed by FakeHandler2", HandlerId));
      }
      else
      {
        return Task.FromResult(IntegrationResult.Fail("Failed in FakeHandler2", "Test failure", HandlerId));
      }
    }
  }

  /// <summary>
  /// Minimal handler with no overrides — tests default behavior of WebhookHandlerBase
  /// </summary>
  public class MinimalHandler : WebhookHandlerBase
  {
    public override string HandlerId => "minimal-handler";
    public override string DisplayName => "Minimal Handler";

    public override Task<IntegrationResult> ProcessAsync(StoreManifest manifest, WebhookContext context)
    {
      return Task.FromResult(IntegrationResult.Ok("OK", HandlerId));
    }
  }
}
