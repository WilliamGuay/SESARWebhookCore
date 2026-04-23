using SecureExchangesSDK.Helpers;
using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core.Auth;
using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SESARWebHook.Connectors.Template
{
  public class TemplateConnector : IIntegrationConnector
  {
    private byte[] _key;
    private byte[] _iv;

    public string ConnectorId => "onedrive";
    public string DisplayName => "OneDrive Connector";
    public string Description => "Connecteur OneDrive - s'occupe de la synchronisation des données avec le service de OneDrive";

    /// <summary>
    /// Version de votre connecteur
    /// </summary>
    public string Version => "1.0.0";

    /// <summary>
    /// Liste des paramètres OBLIGATOIRES dans Web.config
    /// Le système vérifiera que ces clés existent avant d'utiliser le connecteur.
    ///
    /// Dans Web.config, ces paramètres seront :
    ///   Connector:template:ApiUrl
    ///   Connector:template:ApiKey
    /// </summary>
    public IEnumerable<string> RequiredConfigurationKeys => new[]
    {
            "ClientId",
            "ClientSecret",
            "TenantId"
    };

    private OAuth2ClientCredentialsHelper _authHelper;

    /// <summary>
    /// Initialise le connecteur avec les paramètres de configuration.
    ///
    /// Les paramètres viennent de Web.config, format :
    ///   Connector:{ConnectorId}:{Paramètre}
    ///
    /// Exemple pour ce connecteur :
    ///   <add key="Connector:template:ApiUrl" value="https://api.exemple.com" />
    ///   <add key="Connector:template:ApiKey" value="ma_cle_api" />
    /// </summary>
    /// <param name="settings">Dictionnaire clé/valeur des paramètres</param>
    public void Initialize(Dictionary<string, string> settings)
    {
      // Récupérer les paramètres (avec valeurs par défaut si absent)
      var clientId = settings.ContainsKey("ClientId") ? settings["ClientId"] : "";
      var clientSecret = settings.ContainsKey("ClientSecret") ? settings["ClientSecret"] : "";
      var tenantId = settings.ContainsKey("TenantId") ? settings["TenantId"] : "";

      var keys = (settings.ContainsKey("PrivateAESKey") ? settings["PrivateAESKey"] : "").Split('_');
      _key = Convert.FromBase64String(keys[0]);
      _iv = Convert.FromBase64String(keys[1]);

      if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
      {
        // Utiliser le helper OAuth2 du Core
        _authHelper = OAuth2ClientCredentialsHelper.ForOneDrive(
            tenantId,
            clientId,
            clientSecret
        );
      }
    }

    public Task<bool> ValidateConfigurationAsync(Dictionary<string, string> settings)
    {
      // Vérifier les paramètres obligatoires
      foreach (var key in RequiredConfigurationKeys)
      {
        if (!settings.ContainsKey(key) || string.IsNullOrWhiteSpace(settings[key]))
        {
          return Task.FromResult(false);
        }
      }

      return Task.FromResult(true);
    }

    public async Task<bool> TestConnectionAsync()
    {
      try
      {
        if (_authHelper == null)
        {
          return false;
        }

        return await _authHelper.TestConnectionAsync();
      }
      catch
      {
        return false;
      }
    }

    public async Task<IntegrationResult> ProcessManifestAsync(StoreManifest manifest, WebhookContext context)
    {
      return IntegrationResult.Ok();
    }

    private async Task<IntegrationResult> CreateFolder(string accessToken, HttpClient client, Uri siteUri, string folderName)
    {
      client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", accessToken);
      client.DefaultRequestHeaders.Accept.Add(
          new MediaTypeWithQualityHeaderValue("application/json"));

      var createFolderRequest = $"https://graph.microsoft.com/v1.0/sites/{siteUri.Host}/drive/root/children"; // TODO: Changer le line pour qu'il soit compatible avec onedrive (trouver le pattern)
      var content = new StringContent($"{{ \"name\": \"{folderName}\", \"folder\": {{}} }}");
      content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

      var response = await client.PostAsync(createFolderRequest, content);
      var responseContent = await response.Content.ReadAsStringAsync();

      return IntegrationResult.Ok(responseContent);
    }

    // TODO: Vérification de l'espace restant dans le drive où le fichier sera uploadé afin de s'assurer 
    private async Task<IntegrationResult> UploadFile(HttpClient client, string accessToken, Uri siteUri, StoreManifest manifest, int fileIndex, string folderName)
    {
      string filePath = manifest.FilesLocation[fileIndex].FullPath;
      string fileName = manifest.FilesMetaData[fileIndex].RealFileName;

      bool deleteDecryptedFile = false;

      if (!File.Exists(filePath))
      {
        deleteDecryptedFile = true;
        CryptoHelper.DecryptFile(filePath + ".secf", filePath, _key, _iv);
      }

      await UploadFileWithUploadSession(client, accessToken, siteUri, filePath, fileName, folderName);

      if (deleteDecryptedFile)
        File.Delete(filePath);

      return IntegrationResult.Ok("File Uploaded");
    }

    //Doc: https://learn.microsoft.com/en-us/graph/api/driveitem-createuploadsession?view=graph-rest-1.0
    private async Task<IntegrationResult> UploadFileWithUploadSession(HttpClient client, string accessToken, Uri siteUri, string filePath, string fileName, string folderName)
    {
      if (File.Exists(filePath))
      {
        client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        string validFileName = Regex.Replace(fileName, "[\"*:<>?/\\|]", " ");
        validFileName = Regex.Replace(validFileName, "  +", " ");

        var createUploadSessionRequest = $"https://graph.microsoft.com/v1.0/sites{siteUri.Host}/drive/root:/{folderName}/{validFileName}:/createUploadSession";
        var uploadSessionRequestContent = new StringContent($"{{ \"item\": {{ \"name\": \"{validFileName}\" }} }}");
        uploadSessionRequestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var response = await client.PostAsync(createUploadSessionRequest, uploadSessionRequestContent);
        var responseContent = await response.Content.ReadFromJsonAsync<UploadSessionResponse>();

        const int UPLOAD_CHUNK_SIZE = 10485760;

        byte[] file = File.ReadAllBytes(filePath);

        for (long i = 0; i < file.LongLength; i += UPLOAD_CHUNK_SIZE)
        {
          long maxByte = Math.Min(i + UPLOAD_CHUNK_SIZE, file.LongLength - 1);
          var content = new ByteArrayContent(file[(int)i..(int)(maxByte + 1)]);
          content.Headers.Add("Content-Length", $"{maxByte - i + 1}");
          content.Headers.Add("Content-Range", $"bytes {i}-{maxByte}/{file.LongLength}");

          var responseUpload = await client.PutAsync(responseContent.UploadUrl, content);
          var responseUloadContent = await responseUpload.Content.ReadAsStringAsync();
        }

        return IntegrationResult.Ok("File uploaded with success");
      }
      return IntegrationResult.Fail("File to upload does not exist");
    }
  }

  public class UploadSessionResponse
  {
    public string Context { get; set; }
    public DateTime ExpirationDateTime { get; set; }
    public string[] NextExcpectedRanges { get; set; }
    public string UploadUrl { get; set; }
  }
}
