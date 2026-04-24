using Microsoft.VisualStudio.TestTools.UnitTesting;
using SESARWebHook.Core.Services;
using SESARWebHook.Tests.Fakes;
using System.Collections.Generic;
using System.Linq;

namespace SESARWebHook.Tests
{
  [TestClass]
  public class HandlerRegistryTests
  {
    private HandlerRegistry _registry;

    [TestInitialize]
    public void Setup()
    {
      _registry = new HandlerRegistry();
    }

    // ──────────────────────────────────────────────
    // Manual Registration
    // ──────────────────────────────────────────────

    [TestMethod]
    public void RegisterHandler_Generic_AddsHandler()
    {
      _registry.RegisterHandler<FakeHandler>();

      Assert.IsTrue(_registry.HandlerExists("fake-handler"));
    }

    [TestMethod]
    public void RegisterHandler_ById_AddsHandler()
    {
      _registry.RegisterHandler("custom-id", typeof(FakeHandler));

      Assert.IsTrue(_registry.HandlerExists("custom-id"));
    }

    [TestMethod]
    public void RegisterHandler_Multiple_AllAvailable()
    {
      _registry.RegisterHandler<FakeHandler>();
      _registry.RegisterHandler<FakeHandler2>();

      var ids = _registry.GetAvailableHandlerIds().ToList();

      Assert.IsTrue(ids.Contains("fake-handler"));
      Assert.IsTrue(ids.Contains("fake-handler-2"));
      Assert.AreEqual(2, ids.Count);
    }

    // ──────────────────────────────────────────────
    // Lookup
    // ──────────────────────────────────────────────

    [TestMethod]
    public void HandlerExists_UnknownId_ReturnsFalse()
    {
      Assert.IsFalse(_registry.HandlerExists("nonexistent"));
    }

    [TestMethod]
    public void GetHandlerInfos_ReturnsDetailsForAll()
    {
      _registry.RegisterHandler<FakeHandler>();
      _registry.RegisterHandler<FakeHandler2>();

      var infos = _registry.GetHandlerInfos().ToList();

      Assert.AreEqual(2, infos.Count);

      var fakeInfo = infos.FirstOrDefault(h => h.HandlerId == "fake-handler");
      Assert.IsNotNull(fakeInfo);
      Assert.AreEqual("Fake Handler", fakeInfo.DisplayName);
      Assert.AreEqual("A fake handler for testing", fakeInfo.Description);
      Assert.AreEqual("1.0.0-test", fakeInfo.Version);
    }

    // ──────────────────────────────────────────────
    // Instance creation & caching
    // ──────────────────────────────────────────────

    [TestMethod]
    public void CreateHandler_ReturnsNewInstance()
    {
      _registry.RegisterHandler<FakeHandler>();

      var handler = _registry.CreateHandler("fake-handler");

      Assert.IsNotNull(handler);
      Assert.AreEqual("fake-handler", handler.HandlerId);
    }

    [TestMethod]
    public void CreateHandler_UnknownId_ReturnsNull()
    {
      var handler = _registry.CreateHandler("nonexistent");

      Assert.IsNull(handler);
    }

    [TestMethod]
    public void GetOrCreateHandler_ReturnsSameInstance_OnSecondCall()
    {
      _registry.RegisterHandler<FakeHandler>();

      var first = _registry.GetOrCreateHandler("fake-handler");
      var second = _registry.GetOrCreateHandler("fake-handler");

      Assert.AreSame(first, second);
    }

    [TestMethod]
    public void GetOrCreateHandler_WithSettings_InitializesHandler()
    {
      _registry.RegisterHandler<FakeHandler>();
      var settings = new Dictionary<string, string>
            {
                { "ApiKey", "my-api-key" },
                { "Endpoint", "https://test.com" }
            };

      var handler = _registry.GetOrCreateHandler("fake-handler", settings) as FakeHandler;

      Assert.IsNotNull(handler);
      Assert.IsNotNull(handler.ExposedSettings);
      Assert.AreEqual("my-api-key", handler.ExposedSettings["ApiKey"]);
    }

    // ──────────────────────────────────────────────
    // Clear & Rescan
    // ──────────────────────────────────────────────

    [TestMethod]
    public void ClearHandlerInstance_RemovesCachedInstance()
    {
      _registry.RegisterHandler<FakeHandler>();
      var first = _registry.GetOrCreateHandler("fake-handler");
      _registry.ClearHandlerInstance("fake-handler");
      var second = _registry.GetOrCreateHandler("fake-handler");

      Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public void ClearAllInstances_RemovesAllCached()
    {
      _registry.RegisterHandler<FakeHandler>();
      _registry.RegisterHandler<FakeHandler2>();
      var first1 = _registry.GetOrCreateHandler("fake-handler");
      var first2 = _registry.GetOrCreateHandler("fake-handler-2");

      _registry.ClearAllInstances();

      var second1 = _registry.GetOrCreateHandler("fake-handler");
      var second2 = _registry.GetOrCreateHandler("fake-handler-2");

      Assert.AreNotSame(first1, second1);
      Assert.AreNotSame(first2, second2);
    }

    [TestMethod]
    public void ScanForHandlers_EmptyFolder_NoError()
    {
      // Scanning a non-existent or empty path should not throw
      _registry.ScanForHandlers(@"C:\NonExistentPath\Handlers");

      // Manual registrations should still work
      _registry.RegisterHandler<FakeHandler>();
      Assert.IsTrue(_registry.HandlerExists("fake-handler"));
    }

    [TestMethod]
    public void Rescan_PreservesManualRegistrations()
    {
      _registry.RegisterHandler<FakeHandler>();

      // Rescan without a valid path should not remove manual registrations
      // (Rescan only adds new handlers from disk, doesn't remove existing ones)
      _registry.Rescan();

      Assert.IsTrue(_registry.HandlerExists("fake-handler"));
    }
  }
}
