using Microsoft.VisualStudio.TestTools.UnitTesting;
using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core.Connectors;
using SESARWebHook.Core.Models;
using SESARWebHook.Core.Services;
using SESARWebHook.Tests.Fakes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SESARWebHook.Tests
{
  [TestClass]
  public class GenericConnectorTests
  {
    private HandlerRegistry _handlerRegistry;
    private GenericConnector _connector;

    [TestInitialize]
    public void Setup()
    {
      _handlerRegistry = new HandlerRegistry();
      _handlerRegistry.RegisterHandler<FakeHandler>();
      _handlerRegistry.RegisterHandler<FakeHandler2>();
      _connector = new GenericConnector(_handlerRegistry);
    }

    // ──────────────────────────────────────────────
    // Properties
    // ──────────────────────────────────────────────

    [TestMethod]
    public void ConnectorId_IsGeneric()
    {
      Assert.AreEqual("generic", _connector.ConnectorId);
    }

    [TestMethod]
    public void DisplayName_IsSet()
    {
      Assert.IsFalse(string.IsNullOrEmpty(_connector.DisplayName));
    }

    [TestMethod]
    public void RequiredConfigurationKeys_IsEmpty()
    {
      Assert.AreEqual(0, _connector.RequiredConfigurationKeys.Count());
    }

    // ──────────────────────────────────────────────
    // Handler listing
    // ──────────────────────────────────────────────

    [TestMethod]
    public void GetAvailableHandlerIds_ReturnsRegistered()
    {
      var ids = _connector.GetAvailableHandlerIds().ToList();

      Assert.IsTrue(ids.Contains("fake-handler"));
      Assert.IsTrue(ids.Contains("fake-handler-2"));
    }

    [TestMethod]
    public void GetHandlerInfos_ReturnsDetailedInfo()
    {
      var infos = _connector.GetHandlerInfos().ToList();

      Assert.AreEqual(2, infos.Count);
    }

    [TestMethod]
    public void GetHandlerRegistry_ReturnsSameInstance()
    {
      var registry = _connector.GetHandlerRegistry();

      Assert.AreSame(_handlerRegistry, registry);
    }

    // ──────────────────────────────────────────────
    // Routing via HandlerId in metadata
    // ──────────────────────────────────────────────

    [TestMethod]
    public async Task ProcessManifestAsync_RoutesToHandler_ViaMetadata()
    {
      var manifest = CreateTestManifest();
      var context = new WebhookContext
      {
        ConnectorId = "generic",
        Metadata = new Dictionary<string, object>
                {
                    { "HandlerId", "fake-handler" }
                }
      };

      var result = await _connector.ProcessManifestAsync(manifest, context);

      Assert.IsTrue(result.Success);
      Assert.AreEqual("Processed by FakeHandler", result.Message);
    }

    [TestMethod]
    public async Task ProcessManifestAsync_RoutesToHandler2_ViaMetadata()
    {
      var manifest = CreateTestManifest();
      var context = new WebhookContext
      {
        ConnectorId = "generic",
        Metadata = new Dictionary<string, object>
                {
                    { "HandlerId", "fake-handler-2" }
                }
      };

      var result = await _connector.ProcessManifestAsync(manifest, context);

      Assert.IsTrue(result.Success);
      Assert.AreEqual("Processed by FakeHandler2", result.Message);
    }

    // ──────────────────────────────────────────────
    // Routing via ConnectorId fallback
    // ──────────────────────────────────────────────

    [TestMethod]
    public async Task ProcessManifestAsync_FallsBackToConnectorId_IfNoMetadata()
    {
      var manifest = CreateTestManifest();
      var context = new WebhookContext
      {
        ConnectorId = "fake-handler",
        Metadata = new Dictionary<string, object>() // No HandlerId
      };

      var result = await _connector.ProcessManifestAsync(manifest, context);

      Assert.IsTrue(result.Success);
    }

    // ──────────────────────────────────────────────
    // Error handling
    // ──────────────────────────────────────────────

    [TestMethod]
    public async Task ProcessManifestAsync_UnknownHandler_ReturnsFail()
    {
      var manifest = CreateTestManifest();
      var context = new WebhookContext
      {
        ConnectorId = "generic",
        Metadata = new Dictionary<string, object>
                {
                    { "HandlerId", "nonexistent-handler" }
                }
      };

      var result = await _connector.ProcessManifestAsync(manifest, context);

      Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public async Task ProcessManifestAsync_NoHandlerId_ReturnsFail()
    {
      var manifest = CreateTestManifest();
      var context = new WebhookContext
      {
        ConnectorId = "generic", // Not a valid handler
        Metadata = new Dictionary<string, object>()
      };

      var result = await _connector.ProcessManifestAsync(manifest, context);

      Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public async Task ProcessManifestAsync_HandlerFails_PropagatesFailure()
    {
      // Make the handler fail
      var handler = _handlerRegistry.GetOrCreateHandler("fake-handler") as FakeHandler;
      handler.ShouldSucceed = false;

      var manifest = CreateTestManifest();
      var context = new WebhookContext
      {
        ConnectorId = "generic",
        Metadata = new Dictionary<string, object>
                {
                    { "HandlerId", "fake-handler" }
                }
      };

      var result = await _connector.ProcessManifestAsync(manifest, context);

      Assert.IsFalse(result.Success);
    }

    // ──────────────────────────────────────────────
    // Initialize & Validate
    // ──────────────────────────────────────────────

    [TestMethod]
    public async Task ValidateConfigurationAsync_AlwaysTrue()
    {
      var result = await _connector.ValidateConfigurationAsync(new Dictionary<string, string>());

      Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task TestConnectionAsync_ReturnsTrue_WhenHandlersExist()
    {
      var result = await _connector.TestConnectionAsync();

      Assert.IsTrue(result);
    }

    [TestMethod]
    public void Initialize_DoesNotThrow()
    {
      // Should not throw even with empty settings
      _connector.Initialize(new Dictionary<string, string>());
    }

    // ──────────────────────────────────────────────
    // Constructor without registry
    // ──────────────────────────────────────────────

    [TestMethod]
    public void Constructor_NoRegistry_CreatesDefaultRegistry()
    {
      var connector = new GenericConnector();

      Assert.IsNotNull(connector.GetHandlerRegistry());
      Assert.AreEqual(0, connector.GetAvailableHandlerIds().Count());
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private StoreManifest CreateTestManifest()
    {
      // StoreManifest is from the SecureExchanges SDK
      // Use only properties that exist on the actual SDK model
      return new StoreManifest();
    }
  }
}
