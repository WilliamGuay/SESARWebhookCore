using SESARWebHook.Core.Auth;
using SESARWebHook.Core.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SESARLightUtils.StorageServiceHelpers
{
  public class SharePointServiceHelper : SESARStorageServicesOperationHelper
  {
    private OAuth2ClientCredentialsHelper _authHelper;
    private string accessToken;
    private Uri siteUri;

    public SharePointServiceHelper(Dictionary<string, string> settings)
    {
      var siteUrl = settings.ContainsKey("SiteUrl") ? settings["SiteUrl"] : "";
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
      siteUri = new Uri(siteUrl);
    }

    public SharePointServiceHelper(Dictionary<string, string> settings, string kekPath)
      : this(settings)
    {
      base.kekPath = kekPath;
    }

    protected override async Task Authenticate()
    {
      accessToken = await _authHelper.GetAccessTokenAsync();
    }

    public async override Task<string> UploadFile(byte[] file, string fileName, string folderName, bool overrideExistingFile = false)
    {
      if (file == null)
        throw new ArgumentNullException(nameof(file));
      if (file.Length == 0)
        throw new ArgumentException("No bytes to upload");

      using (var client = new HttpClient())
      {
        return await UploadFileWithUploadSession(client, accessToken, siteUri, file, folderName, fileName);
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

    private async Task<string> UploadFileWithUploadSession(HttpClient client, string accessToken, Uri siteUri, byte[] fileBytes, string folderName, string fileName)
    {
      try
      {
        client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var createUploadSessionRequest = $"https://graph.microsoft.com/v1.0/sites{siteUri.Host}/drive/root:/{folderName}/{fileName}:/createUploadSession";
        var uploadSessionRequestContent = new StringContent($"{{ \"item\": {{ \"name\": \"{fileName}\" }} }}");
        uploadSessionRequestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var response = await client.PostAsync(createUploadSessionRequest, uploadSessionRequestContent);
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

    public override Task<bool> RotateHeaderKey(string path, byte[] oldKek, byte[] newKek)
    {
      throw new NotImplementedException();
    }

    // TODO: la gestion du KEK n'est pas encore portée sur SharePoint.
    // Ces trois membres sont des ébauches ajoutées pour rétablir la compilation
    // après l'ajout des membres abstraits dans SESARStorageServicesOperationHelper.
    public override Task<byte[]> GetKek(string userKey)
    {
      throw new NotImplementedException();
    }

    public override Task<IntegrationResult> GenerateAndUploadKek(string userKey)
    {
      throw new NotImplementedException();
    }

    public override Task<List<string>> GetAllHeadersPaths()
    {
      throw new NotImplementedException();
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
