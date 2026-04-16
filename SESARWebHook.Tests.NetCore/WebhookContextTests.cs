using Microsoft.VisualStudio.TestTools.UnitTesting;
using SESARWebHook.Core.Models;
using System;

namespace SESARWebHook.Tests
{
  [TestClass]
  public class WebhookContextTests
  {
    [TestMethod]
    public void Constructor_GeneratesRequestId()
    {
      var context = new WebhookContext();

      Assert.IsFalse(string.IsNullOrEmpty(context.RequestId));
      // Should be a valid GUID
      Assert.IsTrue(Guid.TryParse(context.RequestId, out _));
    }

    [TestMethod]
    public void Constructor_SetsReceivedAt()
    {
      var before = DateTime.UtcNow;
      var context = new WebhookContext();
      var after = DateTime.UtcNow;

      Assert.IsTrue(context.ReceivedAt >= before && context.ReceivedAt <= after);
    }

    [TestMethod]
    public void Constructor_InitializesHeaders()
    {
      var context = new WebhookContext();

      Assert.IsNotNull(context.Headers);
      Assert.AreEqual(0, context.Headers.Count);
    }

    [TestMethod]
    public void Constructor_InitializesMetadata()
    {
      var context = new WebhookContext();

      Assert.IsNotNull(context.Metadata);
      Assert.AreEqual(0, context.Metadata.Count);
    }

    [TestMethod]
    public void TwoContexts_HaveDifferentRequestIds()
    {
      var ctx1 = new WebhookContext();
      var ctx2 = new WebhookContext();

      Assert.AreNotEqual(ctx1.RequestId, ctx2.RequestId);
    }

    [TestMethod]
    public void ConnectorId_CanBeSet()
    {
      var context = new WebhookContext { ConnectorId = "zoho-crm" };

      Assert.AreEqual("zoho-crm", context.ConnectorId);
    }

    [TestMethod]
    public void Metadata_CanStoreHandlerId()
    {
      var context = new WebhookContext();
      context.Metadata["HandlerId"] = "my-handler";

      Assert.AreEqual("my-handler", context.Metadata["HandlerId"]);
    }

    [TestMethod]
    public void RawPayload_CanBeSet()
    {
      var context = new WebhookContext { RawPayload = "{\"test\": true}" };

      Assert.AreEqual("{\"test\": true}", context.RawPayload);
    }
  }
}
