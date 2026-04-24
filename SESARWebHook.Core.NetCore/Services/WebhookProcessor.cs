using SecureExchangesSDK.Helpers;
using SecureExchangesSDK.Models.Messenging;
using SecureExchangesSDK.Models.Transport;
using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Models;
using System;
using System.Threading.Tasks;

namespace SESARWebHook.Core.Services
{
  /// <summary>
  /// Main service for processing SESAR webhooks
  /// </summary>
  public class WebhookProcessor : IWebhookProcessor
  {
    private readonly ConnectorRegistry _registry;
    private readonly string _encryptionKey;
    private readonly string _encryptionIv;

    public WebhookProcessor(ConnectorRegistry registry, string encryptionKey, string encryptionIv)
    {
      _registry = registry ?? throw new ArgumentNullException(nameof(registry));
      _encryptionKey = encryptionKey ?? throw new ArgumentNullException(nameof(encryptionKey));
      _encryptionIv = encryptionIv ?? throw new ArgumentNullException(nameof(encryptionIv));
    }

    /// <summary>
    /// Validates the webhook authentication using SHA512 hash
    /// </summary>
    public bool ValidateAuthentication(string hashKey)
    {
      if (string.IsNullOrEmpty(hashKey))
        return false;

      var expectedHash = CryptoHelper.GetSHA512HashOfString(_encryptionKey);
      return string.Equals(hashKey, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decrypts the webhook payload
    /// </summary>
    public string DecryptPayload(string cryptedObject)
    {
      if (string.IsNullOrEmpty(cryptedObject))
        throw new ArgumentException("Encrypted payload cannot be null or empty", nameof(cryptedObject));

      var keyBytes = Convert.FromBase64String(_encryptionKey);
      var ivBytes = Convert.FromBase64String(_encryptionIv);
      var encryptedBytes = Convert.FromBase64String(cryptedObject);

      return CryptoHelper.DecryptStringFromBytes(encryptedBytes, keyBytes, ivBytes);
    }

    /// <summary>
    /// Processes an incoming webhook request
    /// </summary>
    public async Task<IntegrationResult> ProcessWebhookAsync(SesarWebHook webhookData, string connectorId)
    {
      var context = new WebhookContext
      {
        ConnectorId = connectorId
      };

      try
      {
        // Validate authentication
        if (!ValidateAuthentication(webhookData.HashKey))
        {
          return IntegrationResult.Fail(
              "Authentication failed",
              "Invalid hash key",
              connectorId
          );
        }

        // Decrypt the payload
        string jsonPayload;
        try
        {
          jsonPayload = DecryptPayload(webhookData.CryptedObject);
          context.RawPayload = jsonPayload;
        }
        catch (Exception ex)
        {
          return IntegrationResult.Fail(
              "Decryption failed",
              ex.Message,
              connectorId
          );
        }

        // Deserialize the manifest
        StoreManifest manifest;
        try
        {
          manifest = SerializationHelper.DeserializeFromJson<StoreManifest>(jsonPayload);
        }
        catch (Exception ex)
        {
          return IntegrationResult.Fail(
              "Deserialization failed",
              ex.Message,
              connectorId
          );
        }

        // Get the connector
        var connector = _registry.GetOrCreateConnector(connectorId);
        if (connector == null)
        {
          return IntegrationResult.Fail(
              $"Connector not found: {connectorId}",
              $"Available connectors: {string.Join(", ", _registry.GetAvailableConnectorIds())}",
              connectorId
          );
        }

        // Process with the connector
        return await connector.ProcessManifestAsync(manifest, context);
      }
      catch (Exception ex)
      {
        return IntegrationResult.Fail(
            "Unexpected error during processing",
            ex.ToString(),
            connectorId
        );
      }
    }

    /// <summary>
    /// Processes a webhook with multiple connectors
    /// </summary>
    public async Task<IntegrationResult[]> ProcessWebhookWithMultipleConnectorsAsync(
        SesarWebHook webhookData,
        params string[] connectorIds)
    {
      var results = new IntegrationResult[connectorIds.Length];

      for (int i = 0; i < connectorIds.Length; i++)
      {
        results[i] = await ProcessWebhookAsync(webhookData, connectorIds[i]);
      }

      return results;
    }
  }
}
