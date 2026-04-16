using Microsoft.VisualStudio.TestTools.UnitTesting;
using SESARWebHook.Core.Models;
using System;

namespace SESARWebHook.Tests
{
  [TestClass]
  public class IntegrationResultTests
  {
    // ──────────────────────────────────────────────
    // Factory: Ok
    // ──────────────────────────────────────────────

    [TestMethod]
    public void Ok_DefaultMessage_ReturnsSuccess()
    {
      var result = IntegrationResult.Ok();

      Assert.IsTrue(result.Success);
      Assert.IsNotNull(result.Message);
      Assert.IsNull(result.ErrorDetails);
    }

    [TestMethod]
    public void Ok_CustomMessage_SetsMessage()
    {
      var result = IntegrationResult.Ok("Custom message", "my-connector");

      Assert.IsTrue(result.Success);
      Assert.AreEqual("Custom message", result.Message);
      Assert.AreEqual("my-connector", result.ConnectorId);
    }

    [TestMethod]
    public void Ok_SetsTimestamp()
    {
      var before = DateTime.UtcNow;
      var result = IntegrationResult.Ok();
      var after = DateTime.UtcNow;

      Assert.IsTrue(result.Timestamp >= before && result.Timestamp <= after);
    }

    // ──────────────────────────────────────────────
    // Factory: Fail
    // ──────────────────────────────────────────────

    [TestMethod]
    public void Fail_SetsErrorDetails()
    {
      var result = IntegrationResult.Fail("Something went wrong", "Stack trace here", "zoho-crm");

      Assert.IsFalse(result.Success);
      Assert.AreEqual("Something went wrong", result.Message);
      Assert.AreEqual("Stack trace here", result.ErrorDetails);
      Assert.AreEqual("zoho-crm", result.ConnectorId);
    }

    [TestMethod]
    public void Fail_NullErrorDetails_IsOk()
    {
      var result = IntegrationResult.Fail("Error occurred");

      Assert.IsFalse(result.Success);
      Assert.IsNull(result.ErrorDetails);
    }

    // ──────────────────────────────────────────────
    // Metadata
    // ──────────────────────────────────────────────

    [TestMethod]
    public void Metadata_CanStoreAdditionalData()
    {
      var result = IntegrationResult.Ok();
      result.Metadata = new System.Collections.Generic.Dictionary<string, object>
            {
                { "RecordId", "12345" },
                { "Url", "https://example.com/record/12345" }
            };

      Assert.AreEqual("12345", result.Metadata["RecordId"]);
    }

    // ──────────────────────────────────────────────
    // Items tracking
    // ──────────────────────────────────────────────

    [TestMethod]
    public void ItemsProcessed_DefaultsToZero()
    {
      var result = IntegrationResult.Ok();

      Assert.AreEqual(0, result.ItemsProcessed);
      Assert.AreEqual(0, result.ItemsFailed);
    }

    [TestMethod]
    public void ItemsProcessed_CanBeSet()
    {
      var result = IntegrationResult.Ok();
      result.ItemsProcessed = 5;
      result.ItemsFailed = 1;

      Assert.AreEqual(5, result.ItemsProcessed);
      Assert.AreEqual(1, result.ItemsFailed);
    }

    // ──────────────────────────────────────────────
    // ExternalReferenceId
    // ──────────────────────────────────────────────

    [TestMethod]
    public void ExternalReferenceId_CanBeSet()
    {
      var result = IntegrationResult.Ok();
      result.ExternalReferenceId = "EXT-REF-001";

      Assert.AreEqual("EXT-REF-001", result.ExternalReferenceId);
    }
  }
}
