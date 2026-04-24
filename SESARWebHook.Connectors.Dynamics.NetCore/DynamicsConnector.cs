using Newtonsoft.Json;
using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core.Auth;
using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace SESARWebHook.Connectors.Dynamics
{
  /// <summary>
  /// Connector for Microsoft Dynamics 365 integration.
  /// Creates records in Dynamics 365 from SESAR manifests.
  ///
  /// EXEMPLE DE CONFIGURATION (connectors.secrets.json):
  /// {
  ///   "dynamics": {
  ///     "TenantId": "your-tenant-id",
  ///     "ClientId": "your-client-id",
  ///     "ClientSecret": "your-client-secret",
  ///     "ResourceUrl": "https://yourorg.crm.dynamics.com",
  ///     "EntityName": "contacts",
  ///     "ApiVersion": "v9.2"
  ///   }
  /// }
  /// </summary>
  public class DynamicsConnector : IIntegrationConnector
  {
    private OAuth2ClientCredentialsHelper _authHelper;
    private string _resourceUrl;
    private string _entityName;
    private string _apiVersion;

    public string ConnectorId => "dynamics";
    public string DisplayName => "Microsoft Dynamics 365";
    public string Description => "Synchronizes SESAR exchanges with Microsoft Dynamics 365 CRM";
    public string Version => "1.0.0";

    public IEnumerable<string> RequiredConfigurationKeys => new[]
    {
            "ResourceUrl",
            "ClientId",
            "ClientSecret",
            "TenantId"
        };

    public void Initialize(Dictionary<string, string> settings)
    {
      _resourceUrl = settings.ContainsKey("ResourceUrl") ? settings["ResourceUrl"] : "";
      _entityName = settings.ContainsKey("EntityName") ? settings["EntityName"] : "contacts";
      _apiVersion = settings.ContainsKey("ApiVersion") ? settings["ApiVersion"] : "v9.2";

      var tenantId = settings.ContainsKey("TenantId") ? settings["TenantId"] : "";
      var clientId = settings.ContainsKey("ClientId") ? settings["ClientId"] : "";
      var clientSecret = settings.ContainsKey("ClientSecret") ? settings["ClientSecret"] : "";

      if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
      {
        // Utiliser le helper OAuth2 du Core
        _authHelper = OAuth2ClientCredentialsHelper.ForDynamics365(
            tenantId,
            clientId,
            clientSecret,
            _resourceUrl
        );
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

      try
      {
        var token = await _authHelper.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
          return false;
        }

        // Test API call to WhoAmI endpoint
        using (var client = new HttpClient())
        {
          client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", token);
          client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
          client.DefaultRequestHeaders.Add("OData-Version", "4.0");

          var response = await client.GetAsync($"{_resourceUrl}/api/data/{_apiVersion}/WhoAmI");
          return response.IsSuccessStatusCode;
        }
      }
      catch
      {
        return false;
      }
    }

    public async Task<IntegrationResult> ProcessManifestAsync(StoreManifest manifest, WebhookContext context)
    {
      try
      {
        var accessToken = await _authHelper.GetAccessTokenAsync();

        using (var client = new HttpClient())
        {
          client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", accessToken);
          client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
          client.DefaultRequestHeaders.Add("OData-Version", "4.0");
          client.DefaultRequestHeaders.Accept.Add(
              new MediaTypeWithQualityHeaderValue("application/json"));

          // Create the Dynamics record from manifest
          var record = MapManifestToDynamicsRecord(manifest, context);

          var url = $"{_resourceUrl}/api/data/{_apiVersion}/{_entityName}";
          var json = JsonConvert.SerializeObject(record);
          var content = new StringContent(json, Encoding.UTF8, "application/json");

          var response = await client.PostAsync(url, content);

          if (!response.IsSuccessStatusCode)
          {
            var errorContent = await response.Content.ReadAsStringAsync();
            return IntegrationResult.Fail(
                $"Dynamics 365 API error: {response.StatusCode}",
                errorContent,
                ConnectorId
            );
          }

          // Get the created record ID from the OData-EntityId header
          string recordId = null;
          if (response.Headers.TryGetValues("OData-EntityId", out var entityIds))
          {
            var entityId = string.Join("", entityIds);
            // Extract GUID from the URL
            var match = System.Text.RegularExpressions.Regex.Match(entityId, @"\(([^)]+)\)");
            if (match.Success)
            {
              recordId = match.Groups[1].Value;
            }
          }

          return new IntegrationResult
          {
            Success = true,
            Message = $"Record created in Dynamics 365 ({_entityName})",
            ConnectorId = ConnectorId,
            ExternalReferenceId = recordId,
            ItemsProcessed = 1,
            Metadata = new Dictionary<string, object>
                        {
                            { "EntityName", _entityName },
                            { "RecordId", recordId },
                            { "ResourceUrl", _resourceUrl }
                        }
          };
        }
      }
      catch (OAuth2Exception ex)
      {
        return IntegrationResult.Fail(
            "Dynamics 365 authentication failed",
            ex.ToString(),
            ConnectorId
        );
      }
      catch (Exception ex)
      {
        return IntegrationResult.Fail(
            "Failed to create record in Dynamics 365",
            ex.ToString(),
            ConnectorId
        );
      }
    }

    private Dictionary<string, object> MapManifestToDynamicsRecord(StoreManifest manifest, WebhookContext context)
    {
      var record = new Dictionary<string, object>();

      // Default field mappings - customize based on your Dynamics 365 setup
      record["description"] = $"Synchronized from SESAR at {context.ReceivedAt:yyyy-MM-dd HH:mm:ss}\nRequest ID: {context.RequestId}";

      // Store SESAR reference in a custom field if available
      // Note: Custom fields in Dynamics typically have a publisher prefix
      // Example: record["new_sesarrequestid"] = context.RequestId;

      return record;
    }
  }
}
