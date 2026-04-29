using MimeKit;
using Newtonsoft.Json;
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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace SESARWebHook.Connectors.SharePoint
{
  /// <summary>
  /// Connector for SharePoint Online integration.
  /// Uploads files and creates folders SESAR manifests.
  ///
  /// EXEMPLE DE CONFIGURATION (connectors.secrets.json):
  /// {
  ///   "sharepoint": {
  ///     "TenantId": "your-tenant-id",
  ///     "ClientId": "your-client-id",
  ///     "ClientSecret": "your-client-secret",
  ///     "SiteUrl": "https://yourtenant.sharepoint.com/sites/yoursite",
  ///     "DocumentLibrary": "Documents",
  ///     "ListName": "SESAR Exchanges"
  ///   }
  /// }
  /// </summary>
  public class SharePointConnector : IIntegrationConnector
  {
    private const string Pattern = "[\"*:<>?/\\|]";
    private OAuth2ClientCredentialsHelper _authHelper;
    private string _siteUrl;
    private string _kekPath;
    private byte[] _key;
    private byte[] _iv;

    public string ConnectorId => "sharepoint";
    public string DisplayName => "SharePoint Online";
    public string Description => "Uploads SESAR exchange documents to SharePoint Online document libraries";
    public string Version => "1.0.0";

    public IEnumerable<string> RequiredConfigurationKeys => new[]
    {
            "SiteUrl",
            "ClientId",
            "ClientSecret",
            "TenantId"
        };

    public void Initialize(Dictionary<string, string> settings)
    {
      _siteUrl = settings.ContainsKey("SiteUrl") ? settings["SiteUrl"] : "";
      _kekPath = settings.ContainsKey("KekPath") ? settings["KekPath"] : "";

      var keys = (settings.ContainsKey("PrivateAESKey") ? settings["PrivateAESKey"] : "").Split('_');
      _key = Convert.FromBase64String(keys[0]);
      _iv = Convert.FromBase64String(keys[1]);

      var tenantId = settings.ContainsKey("TenantId") ? settings["TenantId"] : "";
      var clientId = settings.ContainsKey("ClientId") ? settings["ClientId"] : "";
      var clientSecret = settings.ContainsKey("ClientSecret") ? settings["ClientSecret"] : "";

      if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
      {
        // Utiliser le helper OAuth2 du Core
        _authHelper = OAuth2ClientCredentialsHelper.ForSharePoint(
            tenantId,
            clientId,
            clientSecret
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

      return await _authHelper.TestConnectionAsync();
    }

    public async Task<IntegrationResult> ProcessManifestAsync(StoreManifest manifest, WebhookContext context)
    {
      try
      {
        var accessToken = await _authHelper.GetAccessTokenAsync();

        using (var client = new HttpClient())
        {
          var uri = new Uri(_siteUrl);

          string validFolderName = Regex.Replace(manifest.OriginalRecipientInfo.Subject, Pattern, " ");
          validFolderName = Regex.Replace(validFolderName, "  +", " ");

          await CreateFolder(accessToken, client, uri, validFolderName);

          if (manifest.FilesMetaData != null && manifest.FilesMetaData.Count > 0)
          {
            for (int i = 0; i < manifest.FilesMetaData.Count; i++)
              await UploadFile(client, accessToken, uri, manifest, i, validFolderName);
          }

          return IntegrationResult.Ok("Manifest Processed");
        }
      }
      catch (OAuth2Exception ex)
      {
        return IntegrationResult.Fail(
            "SharePoint authentication failed",
            ex.ToString(),
            ConnectorId
        );
      }
      catch (Exception ex)
      {
        return IntegrationResult.Fail(
            "Failed to upload to SharePoint",
            ex.ToString(),
            ConnectorId
        );
      }
    }

    //Doc: https://learn.microsoft.com/en-us/graph/api/driveitem-post-children?view=graph-rest-1.0&tabs=http
    private async Task<IntegrationResult> CreateFolder(string accessToken, HttpClient client, Uri siteUri, string folderName)
    {
      client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", accessToken);
      client.DefaultRequestHeaders.Accept.Add(
          new MediaTypeWithQualityHeaderValue("application/json"));

      var createFolderRequest = $"https://graph.microsoft.com/v1.0/sites/{siteUri.Host}/drive/root/children";
      var content = new StringContent($"{{ \"name\": \"{folderName}\", \"folder\": {{}} }}");
      content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

      var response = await client.PostAsync(createFolderRequest, content);
      var responseContent = await response.Content.ReadAsStringAsync();

      return IntegrationResult.Ok(responseContent);
    }

    private async Task<IntegrationResult> UploadFile(HttpClient client, string accessToken, Uri siteUri, StoreManifest manifest, int fileIndex, string folderName)
    {
      string filePath = manifest.FilesLocation[fileIndex].FullPath;
      string fileName = manifest.FilesMetaData[fileIndex].RealFileName;

      byte[] fileBytes = File.ReadAllBytes(filePath);
      byte[] enFileBytes = new byte[fileBytes.Length];
      byte[] dek = CryptoHelper.GenerateSecureRandomByteArray(32);
      byte[] tag = new byte[16];
      byte[] iv = CryptoHelper.GenerateSecureRandomByteArray(12);
      var aesGcm = new AesGcm(dek, 16);
      aesGcm.Encrypt(iv, fileBytes, enFileBytes, tag);

      byte[] enFileFullBytes = new byte[iv.Length + tag.Length + enFileBytes.Length];
      Buffer.BlockCopy(iv, 0, enFileFullBytes, 0, iv.Length);
      Buffer.BlockCopy(enFileBytes, 0, enFileFullBytes, iv.Length, enFileBytes.Length);
      Buffer.BlockCopy(tag, 0, enFileFullBytes, iv.Length + enFileBytes.Length, tag.Length);

      byte[] itemId = Encoding.UTF8.GetBytes(await UploadFileWithUploadSession(client, accessToken, siteUri, fileBytes, folderName));
      byte[] kek = File.ReadAllBytes(_kekPath);
      byte[] kchk = new byte[12];

      using (var sha512 = SHA512.Create())
      {
        kchk = sha512.ComputeHash(kek)[..12];
      }

      byte[] kiv = CryptoHelper.GenerateSecureRandomByteArray(12);
      byte[] ktag = new byte[16];
      byte[] preparedDek = new byte[dek.Length + 20];

      Buffer.BlockCopy(dek, 0, preparedDek, 0, dek.Length);
      Buffer.BlockCopy(itemId, 0, preparedDek, dek.Length, itemId.Length);

      byte[] enDek = new byte[preparedDek.Length];

      var kAesGcm = new AesGcm(kek, 16);
      kAesGcm.Encrypt(kiv, preparedDek, enDek, ktag);

      byte[] header = new byte[kiv.Length + enDek.Length + ktag.Length + kchk.Length];
      Buffer.BlockCopy(kiv, 0, header, 0, kiv.Length);
      Buffer.BlockCopy(enDek, 0, header, kiv.Length, enDek.Length);
      Buffer.BlockCopy(ktag, 0, header, kiv.Length + enDek.Length, ktag.Length);
      Buffer.BlockCopy(kchk, 0, header, kiv.Length + enDek.Length + ktag.Length, kchk.Length);

      string uploadHeaderRequest = $"https://graph.microsoft.com/v1.0/sites/{siteUri.Host}/drive/root:/{folderName}/{fileName}:/content";
      var content = new ByteArrayContent(header);
      var response = await client.PutAsync(uploadHeaderRequest, content);
      var responeContent = await response.Content.ReadAsStringAsync();

      return IntegrationResult.Ok("File Uploaded");
    }

    //Doc: https://learn.microsoft.com/en-us/graph/api/driveitem-createuploadsession?view=graph-rest-1.0
    private async Task<string> UploadFileWithUploadSession(HttpClient client, string accessToken, Uri siteUri, byte[] fileBytes, string folderName)
    {
      try
      {
        client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var fileName = Guid.NewGuid().ToString();

        var createUploadSessionRequest = $"https://graph.microsoft.com/v1.0/sites/{siteUri.Host}/drive/root:/{folderName}/{fileName}:/createUploadSession";
        var uploadSessionRequestContent = new StringContent($"{{ \"item\": {{ \"name\": \"{fileName}\", \"deferCommit\": false }} }}");
        uploadSessionRequestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var response = await client.PostAsync(createUploadSessionRequest, uploadSessionRequestContent);
        var responseContent = await response.Content.ReadFromJsonAsync<dynamic>();

        const int UPLOAD_CHUNK_SIZE = 10485760;

        for (long i = 0; i < fileBytes[21..].LongLength; i += UPLOAD_CHUNK_SIZE)
        {
          long maxByte = Math.Min(i + UPLOAD_CHUNK_SIZE, fileBytes[20..].LongLength - 1);
          var content = new ByteArrayContent(fileBytes[((int)i + 20)..(int)(maxByte + 1)]);
          content.Headers.Add("Content-Length", $"{maxByte - i + 1}");
          content.Headers.Add("Content-Range", $"bytes {i}-{maxByte}/{fileBytes[20..].LongLength}");

          var responseUpload = await client.PutAsync(responseContent["uploadUrl"], content);
          var responseUloadContent = await responseUpload.Content.ReadAsStringAsync();
        }

        var emptyContent = new ByteArrayContent(Array.Empty<byte>());
        emptyContent.Headers.Add("Content-Length", "0");
        var fileCompletionRequest = await client.PostAsync(responseContent.UploadUrl, emptyContent);
        var fileCompletionRequestResponse = await fileCompletionRequest.Content.ReadFromJsonAsync<dynamic>();

        return fileCompletionRequestResponse.Id;
      }
      catch
      {
        throw new Exception("An error has occured while uploading file.");
      }

    }
  }
}