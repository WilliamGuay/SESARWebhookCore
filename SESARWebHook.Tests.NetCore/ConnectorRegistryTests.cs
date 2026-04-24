using Microsoft.VisualStudio.TestTools.UnitTesting;
using SESARWebHook.Core.Services;
using SESARWebHook.Tests.Fakes;
using System.Collections.Generic;
using System.Linq;

namespace SESARWebHook.Tests
{
  [TestClass]
  public class ConnectorRegistryTests
  {
    private ConnectorRegistry _registry;

    [TestInitialize]
    public void Setup()
    {
      _registry = new ConnectorRegistry();
    }

    // ──────────────────────────────────────────────
    // Registration
    // ──────────────────────────────────────────────

    [TestMethod]
    public void RegisterConnector_Generic_AddsConnector()
    {
      _registry.RegisterConnector<FakeConnector>();

      Assert.IsTrue(_registry.ConnectorExists("fake-connector"));
    }

    [TestMethod]
    public void RegisterConnector_ById_AddsConnector()
    {
      _registry.RegisterConnector("my-custom-id", typeof(FakeConnector));

      Assert.IsTrue(_registry.ConnectorExists("my-custom-id"));
    }

    [TestMethod]
    public void RegisterConnector_DuplicateId_OverwritesSilently()
    {
      _registry.RegisterConnector("test", typeof(FakeConnector));
      _registry.RegisterConnector("test", typeof(FakeConnector2));

      // Should not throw, last registration wins
      Assert.IsTrue(_registry.ConnectorExists("test"));
    }

    // ──────────────────────────────────────────────
    // Lookup
    // ──────────────────────────────────────────────

    [TestMethod]
    public void ConnectorExists_UnknownId_ReturnsFalse()
    {
      Assert.IsFalse(_registry.ConnectorExists("nonexistent"));
    }

    [TestMethod]
    public void GetAvailableConnectorIds_ReturnsRegisteredIds()
    {
      _registry.RegisterConnector<FakeConnector>();
      _registry.RegisterConnector("another", typeof(FakeConnector2));

      var ids = _registry.GetAvailableConnectorIds().ToList();

      Assert.IsTrue(ids.Contains("fake-connector"));
      Assert.IsTrue(ids.Contains("another"));
      Assert.AreEqual(2, ids.Count);
    }

    [TestMethod]
    public void GetConnectorInfos_ReturnsInfoForAllRegistered()
    {
      _registry.RegisterConnector<FakeConnector>();

      var infos = _registry.GetConnectorInfos().ToList();

      Assert.AreEqual(1, infos.Count);
      Assert.AreEqual("fake-connector", infos[0].ConnectorId);
      Assert.AreEqual("Fake Connector", infos[0].DisplayName);
    }

    // ──────────────────────────────────────────────
    // Instance creation & caching
    // ──────────────────────────────────────────────

    [TestMethod]
    public void GetOrCreateConnector_CreatesInstance()
    {
      _registry.RegisterConnector<FakeConnector>();

      var connector = _registry.GetOrCreateConnector("fake-connector");

      Assert.IsNotNull(connector);
      Assert.AreEqual("fake-connector", connector.ConnectorId);
    }

    [TestMethod]
    public void GetOrCreateConnector_ReturnsSameInstance_OnSecondCall()
    {
      _registry.RegisterConnector<FakeConnector>();

      var first = _registry.GetOrCreateConnector("fake-connector");
      var second = _registry.GetOrCreateConnector("fake-connector");

      Assert.AreSame(first, second);
    }

    [TestMethod]
    public void GetOrCreateConnector_UnknownId_ReturnsNull()
    {
      var connector = _registry.GetOrCreateConnector("nonexistent");

      Assert.IsNull(connector);
    }

    [TestMethod]
    public void GetOrCreateConnector_WithSettings_InitializesConnector()
    {
      _registry.RegisterConnector<FakeConnector>();
      var settings = new Dictionary<string, string>
            {
                { "ApiKey", "test-key" },
                { "Endpoint", "https://example.com" }
            };

      var connector = _registry.GetOrCreateConnector("fake-connector", settings) as FakeConnector;

      Assert.IsNotNull(connector);
      Assert.IsTrue(connector.InitializeCalled);
      Assert.AreEqual("test-key", connector.LastSettings["ApiKey"]);
    }

    // ──────────────────────────────────────────────
    // PreCacheConnectorInstance
    // ──────────────────────────────────────────────

    [TestMethod]
    public void PreCacheConnectorInstance_StoresInstance()
    {
      var precached = new FakeConnector { ResultMessage = "I am precached" };
      _registry.RegisterConnector("test-precache", typeof(FakeConnector));
      _registry.PreCacheConnectorInstance("test-precache", precached);

      var result = _registry.GetOrCreateConnector("test-precache") as FakeConnector;

      Assert.AreSame(precached, result);
      Assert.AreEqual("I am precached", result.ResultMessage);
    }

    [TestMethod]
    public void PreCacheConnectorInstance_OverridesExisting()
    {
      _registry.RegisterConnector<FakeConnector>();
      var original = _registry.GetOrCreateConnector("fake-connector");

      var replacement = new FakeConnector { ResultMessage = "replacement" };
      _registry.PreCacheConnectorInstance("fake-connector", replacement);

      var result = _registry.GetOrCreateConnector("fake-connector");

      Assert.AreSame(replacement, result);
    }

    // ──────────────────────────────────────────────
    // Clear
    // ──────────────────────────────────────────────

    [TestMethod]
    public void ClearConnectorInstance_RemovesCachedInstance()
    {
      _registry.RegisterConnector<FakeConnector>();
      var first = _registry.GetOrCreateConnector("fake-connector");
      _registry.ClearConnectorInstance("fake-connector");
      var second = _registry.GetOrCreateConnector("fake-connector");

      // Should be different instances since cache was cleared
      Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public void ClearAllInstances_RemovesAllCached()
    {
      _registry.RegisterConnector<FakeConnector>();
      _registry.RegisterConnector("second", typeof(FakeConnector2));
      var first1 = _registry.GetOrCreateConnector("fake-connector");
      var first2 = _registry.GetOrCreateConnector("second");

      _registry.ClearAllInstances();

      var second1 = _registry.GetOrCreateConnector("fake-connector");
      var second2 = _registry.GetOrCreateConnector("second");

      Assert.AreNotSame(first1, second1);
      Assert.AreNotSame(first2, second2);
    }
  }
}
