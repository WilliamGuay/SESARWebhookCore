using Newtonsoft.Json;
using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Connectors.ZohoCRM.Models;
using SESARWebHook.Core.Auth;
using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace SESARWebHook.Connectors.ZohoCRM
{
  /// <summary>
  /// Connector for Zoho CRM integration.
  /// Creates records in Zoho CRM from SESAR manifests.
  ///
  /// EXEMPLE DE CONFIGURATION (connectors.secrets.json):
  /// {
  ///   "zoho-crm": {
  ///     "ClientId": "your-client-id",
  ///     "ClientSecret": "your-client-secret",
  ///     "RefreshToken": "your-refresh-token",
  ///     "AccountsDomain": "https://accounts.zoho.com",
  ///     "Domain": "https://www.zohoapis.com",
  ///     "TargetModule": "Leads"
  ///   }
  /// }
  /// </summary>
  public class ZohoCRMConnector : IIntegrationConnector
  {
    private OAuth2RefreshTokenHelper _authHelper;
    private string _apiDomain;
    private string _targetModule;
    private Dictionary<string, string> _fieldMappings;

    public string ConnectorId => "zoho-crm";
    public string DisplayName => "Zoho CRM";
    public string Description => "Synchronizes SESAR exchanges with Zoho CRM records";
    public string Version => "1.0.0";

    public IEnumerable<string> RequiredConfigurationKeys => new[]
    {
            "ClientId",
            "ClientSecret",
            "RefreshToken",
            "Domain"
        };

    public void Initialize(Dictionary<string, string> settings)
    {
      var clientId = settings.GetValueOrDefault("ClientId", "");
      var clientSecret = settings.GetValueOrDefault("ClientSecret", "");
      var refreshToken = settings.GetValueOrDefault("RefreshToken", "");
      var accountsDomain = settings.GetValueOrDefault("AccountsDomain", "https://accounts.zoho.com");

      _apiDomain = settings.GetValueOrDefault("Domain", "https://www.zohoapis.com");
      _targetModule = settings.GetValueOrDefault("TargetModule", "Leads");

      // Utiliser le helper OAuth2 du Core
      _authHelper = OAuth2RefreshTokenHelper.ForZoho(
          clientId,
          clientSecret,
          refreshToken,
          accountsDomain
      );

      // Parse field mappings (format: "ZohoField:ManifestPath,ZohoField2:ManifestPath2")
      _fieldMappings = new Dictionary<string, string>();
      if (settings.ContainsKey("FieldMappings"))
      {
        var mappings = settings["FieldMappings"].Split(',');
        foreach (var mapping in mappings)
        {
          var parts = mapping.Split(':');
          if (parts.Length == 2)
          {
            _fieldMappings[parts[0].Trim()] = parts[1].Trim();
          }
        }
      }
    }

    public async Task<bool> ValidateConfigurationAsync(Dictionary<string, string> settings)
    {
      foreach (var key in RequiredConfigurationKeys)
      {
        if (!settings.ContainsKey(key) || string.IsNullOrWhiteSpace(settings[key]))
        {
          return false;
        }
      }

      return await Task.FromResult(true);
    }

    public async Task<bool> TestConnectionAsync()
    {
      if (_authHelper == null)
      {
        return false;
      }

      return await _authHelper.TestConnectionAsync();
    }

    public async Task<IntegrationResult> ProcessManifestAsync(StoreManifest manifest, WebhookContext context)
    {
      try
      {
        var accessToken = await _authHelper.GetAccessTokenAsync();

        // Create the Zoho record from manifest
        var record = MapManifestToZohoRecord(manifest, context);

        using (var client = new HttpClient())
        {
          // Zoho utilise "Zoho-oauthtoken" au lieu de "Bearer"
          client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Zoho-oauthtoken", accessToken);

          var url = $"{_apiDomain}/crm/v3/{_targetModule}";
          var requestBody = new { data = new[] { record } };
          var json = JsonConvert.SerializeObject(requestBody);
          var content = new StringContent(json, Encoding.UTF8, "application/json");

          var response = await client.PostAsync(url, content);
          var responseContent = await response.Content.ReadAsStringAsync();

          if (!response.IsSuccessStatusCode)
          {
            return IntegrationResult.Fail(
                $"Zoho CRM API error: {response.StatusCode}",
                responseContent,
                ConnectorId
            );
          }

          var createResponse = JsonConvert.DeserializeObject<ZohoCreateResponse>(responseContent);
          var firstResult = createResponse?.Data?[0];

          if (firstResult?.Status?.ToLower() == "success")
          {
            return new IntegrationResult
            {
              Success = true,
              Message = $"Record created in Zoho CRM {_targetModule}",
              ConnectorId = ConnectorId,
              ExternalReferenceId = firstResult.Details?.Id,
              ItemsProcessed = 1,
              Metadata = new Dictionary<string, object>
                            {
                                { "Module", _targetModule },
                                { "RecordId", firstResult.Details?.Id },
                                { "CreatedTime", firstResult.Details?.CreatedTime }
                            }
            };
          }
          else
          {
            return IntegrationResult.Fail(
                $"Zoho CRM record creation failed: {firstResult?.Message}",
                responseContent,
                ConnectorId
            );
          }
        }
      }
      catch (OAuth2Exception ex)
      {
        return IntegrationResult.Fail(
            "Zoho authentication failed",
            ex.ToString(),
            ConnectorId
        );
      }
      catch (Exception ex)
      {
        return IntegrationResult.Fail(
            "Failed to process manifest for Zoho CRM",
            ex.ToString(),
            ConnectorId
        );
      }
    }

    private Dictionary<string, object> MapManifestToZohoRecord(StoreManifest manifest, WebhookContext context)
    {
      var record = new Dictionary<string, object>();

      // Default mappings - customize based on your Zoho CRM setup
      record["Description"] = $"Synchronized from SESAR at {context.ReceivedAt:yyyy-MM-dd HH:mm:ss}";
      record["SESAR_Request_Id"] = context.RequestId;

      // Add any configured field mappings
      foreach (var mapping in _fieldMappings)
      {
        record[mapping.Key] = $"Mapped from: {mapping.Value}";
      }

      return record;
    }
  }

  /// <summary>
  /// Extension methods for Dictionary
  /// </summary>
  internal static class DictionaryExtensions
  {
    public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
    {
      return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
    }
  }
}
