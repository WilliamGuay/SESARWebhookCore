using Microsoft.VisualStudio.TestTools.UnitTesting;
using SESARWebHook.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SESARWebHook.Tests
{
  [TestClass]
  public class WebhookHandlerBaseTests
  {
    private FakeHandler _handler;

    [TestInitialize]
    public void Setup()
    {
      _handler = new FakeHandler();
    }

    // ──────────────────────────────────────────────
    // Default properties
    // ──────────────────────────────────────────────

    [TestMethod]
    public void HandlerId_IsSet()
    {
      Assert.AreEqual("fake-handler", _handler.HandlerId);
    }

    [TestMethod]
    public void DisplayName_IsSet()
    {
      Assert.AreEqual("Fake Handler", _handler.DisplayName);
    }

    [TestMethod]
    public void Version_CanBeOverridden()
    {
      Assert.AreEqual("1.0.0-test", _handler.Version);
    }

    [TestMethod]
    public void MinimalHandler_HasDefaultVersion()
    {
      var minimal = new MinimalHandler();
      Assert.AreEqual("1.0.0", minimal.Version);
    }

    [TestMethod]
    public void MinimalHandler_HasDefaultDescription()
    {
      var minimal = new MinimalHandler();
      Assert.IsTrue(minimal.Description.Contains("Minimal Handler"));
    }

    // ──────────────────────────────────────────────
    // Initialize
    // ──────────────────────────────────────────────

    [TestMethod]
    public void Initialize_StoresSettings()
    {
      var settings = new Dictionary<string, string>
            {
                { "ApiKey", "test-key-123" },
                { "Endpoint", "https://api.test.com" },
                { "Timeout", "30" },
                { "EnableRetry", "true" }
            };

      _handler.Initialize(settings);

      Assert.AreEqual("test-key-123", _handler.ExposedSettings["ApiKey"]);
      Assert.AreEqual("https://api.test.com", _handler.ExposedSettings["Endpoint"]);
    }

    [TestMethod]
    public void Initialize_NullSettings_DoesNotThrow()
    {
      // Should handle null gracefully
      _handler.Initialize(null);
    }

    // ──────────────────────────────────────────────
    // Configuration helpers
    // ──────────────────────────────────────────────

    [TestMethod]
    public void GetSetting_ExistingKey_ReturnsValue()
    {
      _handler.Initialize(new Dictionary<string, string> { { "MyKey", "MyValue" } });

      Assert.AreEqual("MyValue", _handler.TestGetSetting("MyKey"));
    }

    [TestMethod]
    public void GetSetting_MissingKey_ReturnsNull()
    {
      _handler.Initialize(new Dictionary<string, string>());

      Assert.IsNull(_handler.TestGetSetting("NonExistent"));
    }

    [TestMethod]
    public void GetSettingDefault_MissingKey_ReturnsDefault()
    {
      _handler.Initialize(new Dictionary<string, string>());

      Assert.AreEqual("fallback", _handler.TestGetSettingDefault("NonExistent", "fallback"));
    }

    [TestMethod]
    public void GetSettingDefault_ExistingKey_ReturnsValue()
    {
      _handler.Initialize(new Dictionary<string, string> { { "Key", "Actual" } });

      Assert.AreEqual("Actual", _handler.TestGetSettingDefault("Key", "fallback"));
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void GetRequiredSetting_MissingKey_Throws()
    {
      _handler.Initialize(new Dictionary<string, string>());

      _handler.TestGetRequiredSetting("RequiredKey");
    }

    [TestMethod]
    public void GetRequiredSetting_ExistingKey_ReturnsValue()
    {
      _handler.Initialize(new Dictionary<string, string> { { "RequiredKey", "present" } });

      Assert.AreEqual("present", _handler.TestGetRequiredSetting("RequiredKey"));
    }

    [TestMethod]
    public void GetSettingBool_TrueValue_ReturnsTrue()
    {
      _handler.Initialize(new Dictionary<string, string> { { "Flag", "true" } });

      Assert.IsTrue(_handler.TestGetSettingBool("Flag"));
    }

    [TestMethod]
    public void GetSettingBool_FalseValue_ReturnsFalse()
    {
      _handler.Initialize(new Dictionary<string, string> { { "Flag", "false" } });

      Assert.IsFalse(_handler.TestGetSettingBool("Flag"));
    }

    [TestMethod]
    public void GetSettingBool_InvalidValue_ReturnsFalse()
    {
      // Implementation: bool.TryParse(value, out result) && result
      // For invalid values, TryParse returns false, so result is always false
      _handler.Initialize(new Dictionary<string, string> { { "Flag", "not-a-bool" } });

      Assert.IsFalse(_handler.TestGetSettingBool("Flag", true));
    }

    [TestMethod]
    public void GetSettingBool_MissingKey_ReturnsDefault()
    {
      _handler.Initialize(new Dictionary<string, string>());

      Assert.IsFalse(_handler.TestGetSettingBool("Flag", false));
      Assert.IsTrue(_handler.TestGetSettingBool("Flag", true));
    }

    [TestMethod]
    public void GetSettingInt_ValidValue_ReturnsInt()
    {
      _handler.Initialize(new Dictionary<string, string> { { "Count", "42" } });

      Assert.AreEqual(42, _handler.TestGetSettingInt("Count"));
    }

    [TestMethod]
    public void GetSettingInt_InvalidValue_ReturnsDefault()
    {
      _handler.Initialize(new Dictionary<string, string> { { "Count", "not-a-number" } });

      Assert.AreEqual(99, _handler.TestGetSettingInt("Count", 99));
    }

    [TestMethod]
    public void GetSettingInt_MissingKey_ReturnsDefault()
    {
      _handler.Initialize(new Dictionary<string, string>());

      Assert.AreEqual(0, _handler.TestGetSettingInt("Count"));
      Assert.AreEqual(10, _handler.TestGetSettingInt("Count", 10));
    }

    // ──────────────────────────────────────────────
    // Base64 helper
    // ──────────────────────────────────────────────

    [TestMethod]
    public void DecodeBase64_ValidInput_DecodesCorrectly()
    {
      var original = "Hello, World!";
      var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(original));

      var decoded = _handler.TestDecodeBase64(base64);

      Assert.AreEqual(original, decoded);
    }

    [TestMethod]
    public void DecodeBase64_UnicodeInput_DecodesCorrectly()
    {
      var original = "Bonjour le monde! àéîôü";
      var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(original));

      var decoded = _handler.TestDecodeBase64(base64);

      Assert.AreEqual(original, decoded);
    }

    // ──────────────────────────────────────────────
    // Default implementations (ValidateConfig, TestConnection)
    // ──────────────────────────────────────────────

    [TestMethod]
    public async Task ValidateConfigurationAsync_Default_ReturnsTrue()
    {
      var result = await _handler.ValidateConfigurationAsync(new Dictionary<string, string>());

      Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task TestConnectionAsync_Default_ReturnsTrue()
    {
      var result = await _handler.TestConnectionAsync();

      Assert.IsTrue(result);
    }
  }
}
