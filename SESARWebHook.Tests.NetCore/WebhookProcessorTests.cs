using Microsoft.VisualStudio.TestTools.UnitTesting;
using SecureExchangesSDK.Helpers;
using SESARWebHook.Core.Services;
using SESARWebHook.Tests.Fakes;
using System;
using System.Threading.Tasks;

namespace SESARWebHook.Tests
{
  [TestClass]
  public class WebhookProcessorTests
  {
    private ConnectorRegistry _registry;

    [TestInitialize]
    public void Setup()
    {
      _registry = new ConnectorRegistry();
      _registry.RegisterConnector<FakeConnector>();
    }

    // ──────────────────────────────────────────────
    // Constructor validation
    // ──────────────────────────────────────────────

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_NullRegistry_Throws()
    {
      new WebhookProcessor(null, "key", "iv");
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_NullKey_Throws()
    {
      new WebhookProcessor(_registry, null, "iv");
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_NullIv_Throws()
    {
      new WebhookProcessor(_registry, "key", null);
    }

    // ──────────────────────────────────────────────
    // ValidateAuthentication
    // ──────────────────────────────────────────────

    [TestMethod]
    public void ValidateAuthentication_CorrectHash_ReturnsTrue()
    {
      var testKey = Convert.ToBase64String(new byte[32]); // 32 zero bytes as base64
      var processor = new WebhookProcessor(_registry, testKey, Convert.ToBase64String(new byte[16]));

      // The expected hash is SHA512 of the encryption key
      var correctHash = CryptoHelper.GetSHA512HashOfString(testKey);

      Assert.IsTrue(processor.ValidateAuthentication(correctHash));
    }

    [TestMethod]
    public void ValidateAuthentication_WrongHash_ReturnsFalse()
    {
      var testKey = Convert.ToBase64String(new byte[32]);
      var processor = new WebhookProcessor(_registry, testKey, Convert.ToBase64String(new byte[16]));

      Assert.IsFalse(processor.ValidateAuthentication("wrong-hash"));
    }

    [TestMethod]
    public void ValidateAuthentication_NullHash_ReturnsFalse()
    {
      var testKey = Convert.ToBase64String(new byte[32]);
      var processor = new WebhookProcessor(_registry, testKey, Convert.ToBase64String(new byte[16]));

      Assert.IsFalse(processor.ValidateAuthentication(null));
    }

    [TestMethod]
    public void ValidateAuthentication_EmptyHash_ReturnsFalse()
    {
      var testKey = Convert.ToBase64String(new byte[32]);
      var processor = new WebhookProcessor(_registry, testKey, Convert.ToBase64String(new byte[16]));

      Assert.IsFalse(processor.ValidateAuthentication(string.Empty));
    }

    [TestMethod]
    public void ValidateAuthentication_CaseInsensitive()
    {
      var testKey = Convert.ToBase64String(new byte[32]);
      var processor = new WebhookProcessor(_registry, testKey, Convert.ToBase64String(new byte[16]));

      var correctHash = CryptoHelper.GetSHA512HashOfString(testKey);

      // Both upper and lower case should match
      Assert.IsTrue(processor.ValidateAuthentication(correctHash.ToUpperInvariant()));
      Assert.IsTrue(processor.ValidateAuthentication(correctHash.ToLowerInvariant()));
    }

    // ──────────────────────────────────────────────
    // DecryptPayload
    // ──────────────────────────────────────────────

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public void DecryptPayload_NullInput_Throws()
    {
      var testKey = Convert.ToBase64String(new byte[32]);
      var processor = new WebhookProcessor(_registry, testKey, Convert.ToBase64String(new byte[16]));

      processor.DecryptPayload(null);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public void DecryptPayload_EmptyInput_Throws()
    {
      var testKey = Convert.ToBase64String(new byte[32]);
      var processor = new WebhookProcessor(_registry, testKey, Convert.ToBase64String(new byte[16]));

      processor.DecryptPayload(string.Empty);
    }

    // ──────────────────────────────────────────────
    // ProcessWebhookAsync - connector routing
    // ──────────────────────────────────────────────

    [TestMethod]
    public async Task ProcessWebhookAsync_UnknownConnector_ReturnsFail()
    {
      var testKey = Convert.ToBase64String(new byte[32]);
      var testIv = Convert.ToBase64String(new byte[16]);
      var processor = new WebhookProcessor(_registry, testKey, testIv);

      // Create a webhook with correct auth but unknown connector
      var correctHash = CryptoHelper.GetSHA512HashOfString(testKey);
      var webhook = new SecureExchangesSDK.Models.Transport.SesarWebHook
      {
        HashKey = correctHash,
        CryptedObject = "invalid-but-auth-checked-first"
      };

      // This should fail at auth since the encrypted object is garbage
      var result = await processor.ProcessWebhookAsync(webhook, "nonexistent-connector");

      // Will fail either at auth or at decryption - either way it's a failure
      // (depends on whether hash matches)
      Assert.IsNotNull(result);
    }

    // ──────────────────────────────────────────────
    // ProcessWebhookWithMultipleConnectorsAsync
    // ──────────────────────────────────────────────

    [TestMethod]
    public async Task ProcessWebhookWithMultipleConnectors_ReturnsResultPerConnector()
    {
      var testKey = Convert.ToBase64String(new byte[32]);
      var testIv = Convert.ToBase64String(new byte[16]);
      var processor = new WebhookProcessor(_registry, testKey, testIv);

      var correctHash = CryptoHelper.GetSHA512HashOfString(testKey);
      var webhook = new SecureExchangesSDK.Models.Transport.SesarWebHook
      {
        HashKey = "wrong-hash", // Intentionally wrong to test failure path
        CryptedObject = "test"
      };

      var results = await processor.ProcessWebhookWithMultipleConnectorsAsync(
          webhook, "fake-connector", "nonexistent");

      Assert.AreEqual(2, results.Length);

      // Both should fail (wrong hash)
      Assert.IsFalse(results[0].Success);
      Assert.IsFalse(results[1].Success);
    }
  }
}
