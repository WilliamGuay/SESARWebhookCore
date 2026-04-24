using SecureExchangesSDK.Models.Transport;
using SESARWebHook.Core.Models;
using System.Threading.Tasks;

namespace SESARWebHook.Core.Interfaces
{
  /// <summary>
  /// Interface for the main webhook processing service
  /// </summary>
  public interface IWebhookProcessor
  {
    /// <summary>
    /// Processes an incoming SESAR webhook request
    /// </summary>
    /// <param name="webhookData">The encrypted webhook data from SESAR</param>
    /// <param name="connectorId">The ID of the connector to use for processing</param>
    /// <returns>Result of the processing operation</returns>
    Task<IntegrationResult> ProcessWebhookAsync(SesarWebHook webhookData, string connectorId);

    /// <summary>
    /// Validates the webhook authentication
    /// </summary>
    /// <param name="hashKey">The hash key from the webhook request</param>
    /// <returns>True if authentication is valid</returns>
    bool ValidateAuthentication(string hashKey);

    /// <summary>
    /// Decrypts the webhook payload
    /// </summary>
    /// <param name="cryptedObject">The encrypted payload</param>
    /// <returns>The decrypted JSON string</returns>
    string DecryptPayload(string cryptedObject);
  }
}
