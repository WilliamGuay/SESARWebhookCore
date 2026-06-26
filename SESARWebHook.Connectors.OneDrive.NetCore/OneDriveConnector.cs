using MimeKit;
using SecureExchangesSDK.Helpers;
using SecureExchangesSDK.Models.Messenging;
using SESARLightUtils;
using SESARLightUtils.StorageServiceHelpers;
using SESARWebhook.SESARLightUtils.StorageServiceHelpers;
using SESARWebHook.Core.Auth;
using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Models;
using SESARWebHook.SESARLightUtils.StorageServiceHelpers;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
  public class OneDriveConnector : IIntegrationConnector
  {
    private const string Pattern = "[\"*:<>?/\\|]";
    private OAuth2ClientCredentialsHelper _authHelper;
    private string _kekPath;
    private SESARStorageServicesOperationHelper serviceHelper;
    private string _oneDriveUserEmail;
    private byte[] _key;
    private byte[] _iv;

    public string ConnectorId => "onedrive";
    public string DisplayName => "OneDrive Online";
    public string Description => "Uploads SESAR exchange documents to OneDrive Online document libraries";
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
      _kekPath = settings.ContainsKey("KekPath") ? settings["KekPath"] : "";
      _oneDriveUserEmail = settings.ContainsKey("OneDriveUserEmail") ? settings["OneDriveUserEmail"] : "";

      var keys = (settings.ContainsKey("PrivateAESKey") ? settings["PrivateAESKey"] : "").Split('_');
      _key = Convert.FromBase64String(keys[0]);
      _iv = Convert.FromBase64String(keys[1]);

      var tenantId = settings.ContainsKey("TenantId") ? settings["TenantId"] : "";
      var clientId = settings.ContainsKey("ClientId") ? settings["ClientId"] : "";
      var clientSecret = settings.ContainsKey("ClientSecret") ? settings["ClientSecret"] : "";

      serviceHelper = new OneDriveServiceHelper(settings);

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
          string validFolderName = Regex.Replace(manifest.OriginalRecipientInfo.Subject, Pattern, " ");
          validFolderName = Regex.Replace(validFolderName, "  +", " ");

          await CreateFolder(accessToken, client, _oneDriveUserEmail, validFolderName, manifest);
          await UploadEmail(client, accessToken, _oneDriveUserEmail, validFolderName, manifest);

          if (manifest.FilesMetaData != null && manifest.FilesMetaData.Count > 0)
          {
            for (int i = 0; i < manifest.FilesMetaData.Count; i++)
              await UploadFile(client, accessToken, _oneDriveUserEmail, manifest, manifest.FilesLocation[i].FullPath, manifest.FilesMetaData[i].RealFileName, validFolderName);
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
    private async Task<IntegrationResult> CreateFolder(string accessToken, HttpClient client, string userEmail, string folderName, StoreManifest manifest)
    {
      client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", accessToken);
      client.DefaultRequestHeaders.Accept.Add(
          new MediaTypeWithQualityHeaderValue("application/json"));

      string dayFolderName = manifest.DirectoryPath.Replace("\\" + manifest.OriginalRecipientInfo.Subject, "");

      var createSESARFolderRequest = $"https://graph.microsoft.com/v1.0/users/{userEmail}/drive/root/children";
      var SESARContent = new StringContent($"{{ \"name\": \"SESAR\", \"folder\": {{}} }}");
      SESARContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

      var SESARCreateReponse = await client.PostAsync(createSESARFolderRequest, SESARContent);

      var createDayFolderRequest = $"https://graph.microsoft.com/v1.0/users/{userEmail}/drive/root:/SESAR:/children";
      var dayFolderContent = new StringContent($"{{ \"name\": \"{dayFolderName}\", \"folder\": {{}} }}");
      dayFolderContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

      var dayFolderCreateReponse = await client.PostAsync(createSESARFolderRequest, SESARContent);

      var createFolderRequest = $"https://graph.microsoft.com/v1.0/users/{userEmail}/drive/root:/SESAR/{dayFolderName}:/children";
      var content = new StringContent($"{{ \"name\": \"{folderName}\", \"folder\": {{}} }}");
      content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

      var response = await client.PostAsync(createFolderRequest, content);
      var responseContent = await response.Content.ReadAsStringAsync();

      return IntegrationResult.Ok(responseContent);
    }

    private async Task<IntegrationResult> UploadEmail(HttpClient client, string accessToken, string userEmail, string folderName, StoreManifest manifest)
    {
      string emailHtmlFilePath = manifest.DirectoryPath + "\\EmailSent.html";
      bool deleteHtmlFile = false;
      string emlFilePath = manifest.DirectoryPath + "\\EmailSent.eml";
      try
      {
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

        var builder = new BodyBuilder();
        builder.HtmlBody = Regex.Replace(emailHtml, @"<hr\s*\/>ORIGINAL\sHTML\sENCODED\sMESSAGE\sBELOW.*", "");
        message.Body = builder.ToMessageBody();

        message.WriteTo(emlFilePath);

        if (deleteHtmlFile)
          File.Delete(emailHtmlFilePath);

        await UploadFile(client, accessToken, userEmail, manifest, emlFilePath, "EmailSent.eml", folderName, true);
        File.Delete(emlFilePath);

        return IntegrationResult.Ok();
      }
      catch (Exception ex)
      {
        if (File.Exists(emlFilePath))
        {
          File.Delete(emlFilePath);
        }
        return IntegrationResult.Fail("Erreur lors de la synchronisation du courriel");
      }
    }

    private async Task<IntegrationResult> UploadFile(HttpClient client, string accessToken, string userEmail, StoreManifest manifest, string filePath, string fileName, string folderName, bool isEmail = false)
    {
      bool deleteFile = false;

      if (!isEmail)
      {
        string encryptedFilePath = filePath + ".secf";
        CryptoHelper.DecryptFile(encryptedFilePath, filePath, _key, _iv);
        deleteFile = true;
      }
      byte[] fileBytes = File.ReadAllBytes(filePath);
      if (deleteFile) { File.Delete(filePath); }
      byte[] dek = CryptoHelper.GenerateSecureRandomByteArray(32);
      byte[] enFileFullBytes = SESARCryptoHelper.EncryptBytes(fileBytes, dek);

      byte[] itemId = Encoding.UTF8.GetBytes($"{{ \"ids\": {{ \"OneDrive\": \"{await serviceHelper.UploadFile(enFileFullBytes[12..], fileName + ".secd", folderName, !isEmail)}\" }} }}");
      byte[] kek = File.ReadAllBytes(_kekPath);
      byte[] kchk = new byte[12];

      using (var sha512 = SHA512.Create())
      {
        kchk = sha512.ComputeHash(kek)[..12];
      }

      byte[] kiv = CryptoHelper.GenerateSecureRandomByteArray(12);
      byte[] ktag = new byte[16];
      byte[] preparedDek = new byte[dek.Length + itemId.Length];

      Buffer.BlockCopy(dek, 0, preparedDek, 0, dek.Length);
      Buffer.BlockCopy(itemId, 0, preparedDek, dek.Length, itemId.Length);

      byte[] paramsSize = new byte[Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE];
      BinaryPrimitives.WriteInt64LittleEndian(paramsSize, itemId.LongLength);

      byte[] enDek = new byte[preparedDek.Length];

      var kAesGcm = new AesGcm(kek, 16);
      kAesGcm.Encrypt(kiv, preparedDek, enDek, ktag);

      byte[] header = new byte[Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE + kiv.Length + enDek.Length + ktag.Length + kchk.Length + 12];
      Buffer.BlockCopy(paramsSize, 0, header, 0, Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE);
      Buffer.BlockCopy(kiv, 0, header, Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE, kiv.Length);
      Buffer.BlockCopy(enDek, 0, header, Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE + kiv.Length, enDek.Length);
      Buffer.BlockCopy(ktag, 0, header, Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE + kiv.Length + enDek.Length, ktag.Length);
      Buffer.BlockCopy(kchk, 0, header, Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE + kiv.Length + enDek.Length + ktag.Length, kchk.Length);
      Buffer.BlockCopy(enFileFullBytes[..12], 0, header, Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE + kiv.Length + enDek.Length + ktag.Length + kchk.Length, 12);

      await serviceHelper.UploadFile(header, fileName + ".sech", folderName);

      return IntegrationResult.Ok("File Uploaded");
    }

    public async Task<IntegrationResult> RotateKey()
    {
      string accessToken = await _authHelper.GetAccessTokenAsync();

      byte[] newKek = CryptoHelper.GenerateSecureRandomByteArray(32);
      byte[] aes = CryptoHelper.GenerateSecureRandomByteArray(32);
      byte[] iv = CryptoHelper.GenerateSecureRandomByteArray(12);
      string CSU = Convert.ToBase64String(aes) + "_" + Convert.ToBase64String(iv);

      using (var client = new HttpClient())
      {
        client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        string folderId;

        string listItemsInFolderRequest = $"https://graph.microsoft.com/v1.0/users/wguay@secure-exchanges.info/drive/root:/SESAR:/search(q='')";

        while (!string.IsNullOrEmpty(listItemsInFolderRequest))
        {
          var response = await client.GetAsync(listItemsInFolderRequest);
          response.EnsureSuccessStatusCode();
          var responseContent = response.Content.ReadAsStringAsync();

          using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
          {
            foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
            {
              var driveItem = new DriveItem(item);
              if (Regex.Match(driveItem.Name, @".secd$", RegexOptions.IgnoreCase).Success)
              {
                continue;
              }

              await serviceHelper.RotateHeaderKey($"MTG Dump/{driveItem.Name}", File.ReadAllBytes(_kekPath), newKek);
            }

            listItemsInFolderRequest = doc.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
          }
        }

        return IntegrationResult.Ok();
      }
    }

    public class UploadSessionResponse
    {
      public string Context { get; set; }
      public DateTime ExpirationDate { get; set; }
      public string NextExcpectedDataTime { get; set; }
      public string UploadUrl { get; set; }
    }

    public class DriveItem
    {
      public string Id { get; set; }
      public string Name { get; set; }

      public DriveItem(JsonElement item)
      {
        this.Id = item.GetProperty("id").ToString();
        this.Name = item.GetProperty("name").ToString();
      }
    }
  }
}