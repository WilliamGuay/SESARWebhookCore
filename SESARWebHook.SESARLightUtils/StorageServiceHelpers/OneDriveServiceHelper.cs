using SecureExchangesSDK.Helpers;
using SecureExchangesSDK.Models.Transport;
using SESARLightUtils;
using SESARLightUtils.StorageServiceHelpers;
using SESARWebHook.Core.Auth;
using SESARWebHook.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SESARWebHook.SESARLightUtils.StorageServiceHelpers
{
  public class OneDriveServiceHelper : SESARStorageServicesOperationHelper
  {
    private OAuth2ClientCredentialsHelper _authHelper;
    private string accessToken;
    private Uri siteUri;
    private string _user;

    public OneDriveServiceHelper(Dictionary<string, string> settings)
    {
      _user = settings.ContainsKey("OneDriveUserEmail") ? settings["OneDriveUserEmail"] : "";

      var tenantId = settings.ContainsKey("TenantId") ? settings["TenantId"] : "";
      var clientId = settings.ContainsKey("ClientId") ? settings["ClientId"] : "";
      var clientSecret = settings.ContainsKey("ClientSecret") ? settings["ClientSecret"] : "";

      if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
      {
        _authHelper = OAuth2ClientCredentialsHelper.ForSharePoint(
            tenantId,
            clientId,
            clientSecret
        );
      }
    }

    public OneDriveServiceHelper(Dictionary<string, string> settings, string kekPath)
      : this(settings)
    {
      base.kekPath = kekPath;
    }

    protected override async Task Authenticate()
    {
      accessToken = await _authHelper.GetAccessTokenAsync();
    }

    public override async Task<SEDecryptedSecureFile> DownloadFile(SEDecryptedSecureFileHeader header)
    {
      if (header == null)
        throw new ArgumentNullException(nameof(header));
      if (string.IsNullOrEmpty(accessToken))
        await Authenticate();
      if (header.ServiceIds.ContainsKey("OneDrive"))
      {
        using (var client = new HttpClient())
        {
          client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
          client.DefaultRequestHeaders.Accept.Add(
              new MediaTypeWithQualityHeaderValue("application/json"));

          var dataDownloadUrlRequestResponse = await client.GetFromJsonAsync<DownloadUrlRequestResponse>($"https://graph.microsoft.com/v1.0/users/{_user}/drive/items/{header.ServiceIds["OneDrive"]}?select=@microsoft.graph.downloadUrl");
          using (Stream dataStream = await client.GetStreamAsync(dataDownloadUrlRequestResponse.DownloadUrl))
          {
            return new SEDecryptedSecureFile(await StreamToByteArrayAsync(dataStream), header);
          }
        }
      }
      throw new Exception("An error has occured when downloading file data.");
    }

    public async override Task<string> UploadFile(byte[] file, string fileName, string folderName, bool overrideExistingFile = true)
    {
      if (string.IsNullOrEmpty(accessToken))
        await Authenticate();
      if (file == null)
        throw new ArgumentNullException(nameof(file));
      if (file.Length == 0)
        throw new ArgumentException("No bytes to upload");

      using (var client = new HttpClient())
      {
        return await UploadFileWithUploadSession(client, siteUri, file, folderName, fileName);
      }
    }

    public async Task<SEDecryptedSecureFileHeader> GetHeaderFromPath(string filePath, byte[] key)
    {
      if (string.IsNullOrEmpty(accessToken))
        await Authenticate();
      if (filePath == null)
        throw new ArgumentNullException(nameof(filePath));
      if (key.Length == 0)
        throw new ArgumentException("Empty key");

      using (var client = new HttpClient())
      {
        client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var splittedPath = filePath.Split("/");
        string fileName = splittedPath[splittedPath.Length - 1];
        string folderName = splittedPath[splittedPath.Length - 2];

        var dataDownloadUrlRequestResponse = await client.GetFromJsonAsync<DownloadUrlRequestResponse>($"https://graph.microsoft.com/v1.0/users/{_user}/drive/root:/SESAR/{filePath}?$select=@microsoft.graph.downloadUrl");
        using (Stream dataStream = await client.GetStreamAsync(dataDownloadUrlRequestResponse.DownloadUrl))
        {
          using (var ms = new MemoryStream())
          {
            await dataStream.CopyToAsync(ms);
            var header = ms.ToArray();

            return new SEDecryptedSecureFileHeader(key, header);
          }
        }
      }
    }

    private static async Task<byte[]> StreamToByteArrayAsync(Stream input)
    {
      using (var ms = new MemoryStream())
      {
        await input.CopyToAsync(ms);
        return ms.ToArray();
      }
    }

    private async Task<string> UploadFileWithUploadSession(HttpClient client, Uri siteUri, byte[] fileBytes, string folderName, string fileName, bool isOverride = false)
    {
      try
      {
        client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var createUploadSessionRequest = $"https://graph.microsoft.com/v1.0/users/{_user}/drive/root:/SESAR/{folderName}/{fileName}:/createUploadSession";
        var uploadSessionRequestContent = new StringContent($"{{ \"@microsoft.graph.conflictBehavior\": \"{((isOverride) ? "replace" : "rename")}\" }}");
        uploadSessionRequestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var response = await client.PostAsync(createUploadSessionRequest, uploadSessionRequestContent);
        var responseContentText = await response.Content.ReadAsStringAsync();
        var responseContent = await response.Content.ReadFromJsonAsync<UploadSessionResponse>();

        HttpResponseMessage responseUpload = new HttpResponseMessage();

        for (long i = 0; i < fileBytes.LongLength; i += Constants.UPLOAD_CHUNK_SIZE)
        {
          long maxByte = Math.Min(i + Constants.UPLOAD_CHUNK_SIZE, fileBytes.LongLength - 1);
          var content = new ByteArrayContent(fileBytes[(int)i..(int)(maxByte + 1)]);
          content.Headers.Add("Content-Length", $"{maxByte - i + 1}");
          content.Headers.Add("Content-Range", $"bytes {i}-{maxByte}/{fileBytes.LongLength}");

          responseUpload = await client.PutAsync(responseContent.UploadUrl, content);
        }

        var fileCompletionRequestResponse = await responseUpload.Content.ReadFromJsonAsync<DriveItem>();

        return fileCompletionRequestResponse!.Id;
      }
      catch
      {
        throw new Exception("An error has occured while uploading file.");
      }

    }

    public async override Task<bool> RotateHeaderKey(string filePath, byte[] oldKek, byte[] newKek)
    {
      if (string.IsNullOrEmpty(filePath))
        throw new ArgumentNullException(nameof(filePath));
      if (string.IsNullOrEmpty(accessToken))
        await Authenticate();

      using (var client = new HttpClient())
      {
        client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var splittedPath = filePath.Split("/");
        string fileName = splittedPath[splittedPath.Length - 1];
        string folderName = splittedPath[splittedPath.Length - 2];

        var dataDownloadUrlRequestResponse = await client.GetFromJsonAsync<DownloadUrlRequestResponse>($"https://graph.microsoft.com/v1.0/users/{_user}/drive/root:/SESAR/{filePath}?$select=@microsoft.graph.downloadUrl");
        using (Stream dataStream = await client.GetStreamAsync(dataDownloadUrlRequestResponse.DownloadUrl))
        {
          using (var ms = new MemoryStream())
          {
            await dataStream.CopyToAsync(ms);
            var header = ms.ToArray();

            var dHeaderObject = new SEDecryptedSecureFileHeader(oldKek, header);
            await UploadFile(dHeaderObject.EncryptHeader(newKek), fileName, folderName);
            return true;
          }
        }
      }
      throw new Exception("Something went wrong while rotating key.");
    }
  }
  public class DownloadUrlRequestResponse
  {
    [JsonPropertyName("@microsoft.graph.downloadUrl")]
    public required string DownloadUrl { get; set; }
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
    public long Size { get; set; }
    public object File { get; set; }
  }
}
