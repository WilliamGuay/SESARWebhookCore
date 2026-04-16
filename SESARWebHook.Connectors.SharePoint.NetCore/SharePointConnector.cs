using MimeKit;
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
            clientSecret,
            _siteUrl
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
          await UploadEmail(client, accessToken, uri, manifest, validFolderName);

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

    private async Task<IntegrationResult> UploadEmail(HttpClient client, string accessToken, Uri siteUri, StoreManifest manifest, string folderName)
    {
      string emailHtmlFilePath = manifest.DirectoryPath + "\\EmailSent.html";
      bool deleteHtmlFile = false;

      if (!File.Exists(emailHtmlFilePath))
      {
        deleteHtmlFile = true;
        CryptoHelper.DecryptFile(emailHtmlFilePath + ".secf", emailHtmlFilePath, _key, _iv);
      }

      var message = new MimeMessage();
      message.From.Add(new MailboxAddress(manifest.OriginalRecipientInfo.ContactInfo, manifest.OriginalRecipientInfo.ContactInfo));
      message.Subject = manifest.OriginalRecipientInfo.Subject;
      foreach (var r in manifest.Recipients)
      {
        message.To.Add(new MailboxAddress(r.Email, r.Email));
      }

      string emailHtml = File.ReadAllText(emailHtmlFilePath);

      //Remove trailing HTML from message
      var builder = new BodyBuilder();
      builder.HtmlBody = Regex.Replace(emailHtml, @"<hr\s*\/>ORIGINAL\sHTML\sENCODED\sMESSAGE\sBELOW.*", "");
      message.Body = builder.ToMessageBody();

      string emlFilePath = manifest.DirectoryPath + "\\EmailSent.eml";
      message.WriteTo(emlFilePath);

      if (deleteHtmlFile)
        File.Delete(emailHtmlFilePath);

      await UploadFileWithUploadSession(client, accessToken, siteUri, emlFilePath, "EmailSent.eml", folderName);
      File.Delete(emlFilePath);

      return IntegrationResult.Ok("Email uploaded successfuly");
    }

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