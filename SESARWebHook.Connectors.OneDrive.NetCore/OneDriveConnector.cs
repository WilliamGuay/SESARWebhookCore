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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SESARWebHook.Connectors.OneDrive
{
  /// <summary>
  /// Connector for OneDrive Online integration.
  /// Uploads files and creates folders SESAR manifests.
  ///
  /// EXEMPLE DE CONFIGURATION (connectors.secrets.json):
  /// {
  ///   "onedrive": {
  ///     "TenantId": "your-tenant-id",
  ///     "ClientId": "your-client-id",
  ///     "ClientSecret": "your-client-secret",
  ///     "OneDriveUserEmail": "user@organization.onmicrosoft.com",
  ///     "PrivateAESKey": "base64key_base64iv",
  ///     "UserKey": "base64userkey"
  ///   }
  /// }
  /// </summary>
  public class OneDriveConnector : IIntegrationConnector
  {
    private const string InvalidCharactersPattern = "[\"*:<>?/\\|]";
    private OAuth2ClientCredentialsHelper _authHelper;
    private string _userKey;
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
      "TenantId",
      "ClientId",
      "ClientSecret",
      "OneDriveUserEmail"
    };

    public void Initialize(Dictionary<string, string> settings)
    {
      try
      {
        _userKey = settings.ContainsKey("UserKey") ? settings["UserKey"] : "";
        _oneDriveUserEmail = settings.ContainsKey("OneDriveUserEmail") ? settings["OneDriveUserEmail"] : "";

        // Parse the PrivateAESKey in format "base64key_base64iv"
        var privateAesKey = settings.ContainsKey("PrivateAESKey") ? settings["PrivateAESKey"] : "";
        if (!string.IsNullOrEmpty(privateAesKey))
        {
          var keys = privateAesKey.Split('_');
          if (keys.Length == 2 && !string.IsNullOrEmpty(keys[0]) && !string.IsNullOrEmpty(keys[1]))
          {
            try
            {
              _key = Convert.FromBase64String(keys[0]);
              _iv = Convert.FromBase64String(keys[1]);
            }
            catch (FormatException ex)
            {
              throw new InvalidOperationException("PrivateAESKey must be in format 'base64key_base64iv' with valid base64 strings", ex);
            }
          }
          else
          {
            throw new InvalidOperationException("PrivateAESKey must be in format 'base64key_base64iv' separated by underscore");
          }
        }

        var tenantId = settings.ContainsKey("TenantId") ? settings["TenantId"] : "";
        var clientId = settings.ContainsKey("ClientId") ? settings["ClientId"] : "";
        var clientSecret = settings.ContainsKey("ClientSecret") ? settings["ClientSecret"] : "";

        if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
        {
          // Use OAuth2 helper for OneDrive
          _authHelper = OAuth2ClientCredentialsHelper.ForOneDrive(
              tenantId,
              clientId,
              clientSecret
          );
        }
        else
        {
          throw new InvalidOperationException("OneDrive authentication credentials (TenantId, ClientId, ClientSecret) are missing or empty");
        }

        // Initialize service helper - this might throw if settings are invalid
        serviceHelper = new OneDriveServiceHelper(settings);
      }
      catch (Exception ex)
      {
        throw new InvalidOperationException($"Failed to initialize OneDriveConnector: {ex.Message}", ex);
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
        if (_authHelper == null)
        {
          return IntegrationResult.Fail(
              "OneDrive authentication not configured",
              "OAuth2ClientCredentialsHelper is null. Check that TenantId, ClientId, and ClientSecret are properly configured.",
              ConnectorId
          );
        }

        if (serviceHelper == null)
        {
          return IntegrationResult.Fail(
              "OneDrive service helper not initialized",
              "ServiceHelper is null. Check that OneDriveServiceHelper can be instantiated with the provided settings.",
              ConnectorId
          );
        }

        var accessToken = await _authHelper.GetAccessTokenAsync();

        using (var client = new HttpClient())
        {
          string validFolderName = Regex.Replace(manifest.OriginalRecipientInfo.Subject, InvalidCharactersPattern, " ");
          validFolderName = Regex.Replace(validFolderName, "  +", " ");

          // Les resultats de chaque etape etaient ignores : une exception pendant
          // l'upload du header ne remontait jamais dans la reponse de l'API, qui
          // repondait "Manifest Processed" avec un .sech manquant sur OneDrive.
          var failures = new List<string>();

          var folderResult = await CreateFolder(accessToken, client, _oneDriveUserEmail, validFolderName, manifest);
          if (!folderResult.Success)
            failures.Add($"CreateFolder: {folderResult.Message} | {folderResult.ErrorDetails}");

          var emailResult = await UploadEmail(client, accessToken, _oneDriveUserEmail, validFolderName, manifest);
          if (!emailResult.Success)
            failures.Add($"UploadEmail: {emailResult.Message} | {emailResult.ErrorDetails}");

          if (manifest.FilesMetaData != null && manifest.FilesMetaData.Count > 0)
          {
            for (int i = 0; i < manifest.FilesMetaData.Count; i++)
            {
              var fileResult = await UploadFile(client, accessToken, _oneDriveUserEmail, manifest, manifest.FilesLocation[i].FullPath, manifest.FilesMetaData[i].RealFileName, validFolderName);
              if (!fileResult.Success)
                failures.Add($"UploadFile[{i}] '{manifest.FilesMetaData[i].RealFileName}': {fileResult.Message} | {fileResult.ErrorDetails}");
            }
          }

          if (failures.Count > 0)
          {
            return IntegrationResult.Fail(
                $"Manifest partially processed: {failures.Count} step(s) failed",
                string.Join(Environment.NewLine, failures),
                ConnectorId
            );
          }

          return IntegrationResult.Ok("Manifest Processed");
        }
      }
      catch (OAuth2Exception ex)
      {
        return IntegrationResult.Fail(
            "OneDrive authentication failed",
            // ToDiagnosticString() inclut la reponse brute du serveur OAuth.
            // ErrorDetails porte [JsonIgnore] : ce contenu est journalise, jamais renvoye au client.
            ex.ToDiagnosticString(),
            ConnectorId
        );
      }
      catch (Exception ex)
      {
        return IntegrationResult.Fail(
            "Failed to upload to OneDrive",
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

      var createSESARFolderRequest = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(userEmail)}/drive/root/children";
      var SESARContent = new StringContent($"{{ \"name\": \"SESAR\", \"folder\": {{}}, \"@microsoft.graph.conflictBehavior\": \"replace\" }}");
      SESARContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

      var SESARCreateReponse = await client.PostAsync(createSESARFolderRequest, SESARContent);
      if (!SESARCreateReponse.IsSuccessStatusCode)
      {
        var errorContent = await SESARCreateReponse.Content.ReadAsStringAsync();
        return IntegrationResult.Fail(
            "Failed to create SESAR folder",
            $"HTTP Status: {SESARCreateReponse.StatusCode}, Response: {errorContent}",
            ConnectorId
        );
      }

      var createDayFolderRequest = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(userEmail)}/drive/root:/SESAR:/children";
      var dayFolderContent = new StringContent($"{{ \"name\": \"{dayFolderName}\", \"folder\": {{}} }}");
      dayFolderContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

      var dayFolderCreateReponse = await client.PostAsync(createSESARFolderRequest, SESARContent);
      if (!dayFolderCreateReponse.IsSuccessStatusCode)
      {
        var errorContent = await dayFolderCreateReponse.Content.ReadAsStringAsync();
        return IntegrationResult.Fail(
            "Failed to create day folder",
            $"HTTP Status: {dayFolderCreateReponse.StatusCode}, Response: {errorContent}",
            ConnectorId
        );
      }

      var createFolderRequest = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(userEmail)}/drive/root:/SESAR/{dayFolderName}:/children";
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
        message.Date = manifest.OriginalRecipientInfo.CreateOn;
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

        var fileResult = await UploadFile(client, accessToken, userEmail, manifest, emlFilePath, "EmailSent.eml", folderName, true);
        if (!fileResult.Success)
        {
          return IntegrationResult.Fail(
              "Failed to upload email to OneDrive",
              fileResult.ErrorDetails,
              ConnectorId
          );
        }
          File.Delete(emlFilePath);

        return IntegrationResult.Ok();
      }
      catch (Exception ex)
      {
        if (File.Exists(emlFilePath))
        {
          File.Delete(emlFilePath);
        }
        return IntegrationResult.Fail(
            "Erreur lors de la synchronisation du courriel",
            ex.ToString(),
            ConnectorId
        );
      }
    }

    private async Task<IntegrationResult> UploadFile(HttpClient client, string accessToken, string userEmail, StoreManifest manifest, string filePath, string fileName, string folderName, bool isEmail = false)
    {
      bool deleteFile = false;
      try
      {
        var kek = await serviceHelper.GetKek(_userKey);
        if (kek == null)
        {
          return IntegrationResult.Fail(
              "Failed to retrieve KEK from OneDrive",
              "Wrong UserKey. It may have been changed or might not be correctly written in the configuration.",
              ConnectorId
          );
        }
      }
      catch
      {
        return IntegrationResult.Fail(
              "Failed to retrieve KEK from OneDrive",
              "Wrong UserKey. It may have been changed or might not be correctly written in the configuration.",
              ConnectorId
          );
      }

      try
      {
        if (!isEmail && File.Exists(filePath + ".secf"))
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
        byte[] kek = await serviceHelper.GetKek(_userKey);
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

        var fileResult = await serviceHelper.UploadFile(header, fileName + ".sech", folderName);
        if (string.IsNullOrEmpty(fileResult))
        {
          return IntegrationResult.Fail(
              "Failed to upload email to OneDrive",
              "File upload failed thus no Id available",
              ConnectorId
          );
        }
      }
      catch (Exception ex)
      {
        // Ne jamais supprimer filePath + ".secf" ici : c'est la source chiffree.
        // Seul le fichier temporaire dechiffre doit etre nettoye.
        if (deleteFile && File.Exists(filePath))
        {
          File.Delete(filePath);
        }
        return IntegrationResult.Fail(
            $"Failed to upload '{fileName}' to OneDrive",
            ex.ToString(),
            ConnectorId
        );
      }
      return IntegrationResult.Ok("File Uploaded");
    }
  }
}