using Org.BouncyCastle.Security;
using SecureExchangesSDK.Helpers;
using SESARLightUtils;
using SESARLightUtils.StorageServiceHelpers;
using SESARWebhook.SESARLightUtils.StorageServiceHelpers;
using SESARWebHook.Core.Auth;
using SESARWebHook.Core.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

      kekPath = "utils/kek.pem";

      var tenantId = settings.ContainsKey("TenantId") ? settings["TenantId"] : "";
      var clientId = settings.ContainsKey("ClientId") ? settings["ClientId"] : "";
      var clientSecret = settings.ContainsKey("ClientSecret") ? settings["ClientSecret"] : "";
      var backupKeyPath = settings.ContainsKey("DefaultFailSafePath") ? settings["DefaultFailSafePath"] : "";
      var userKey = settings.ContainsKey("UserKey") ? settings["UserKey"] : "";

      if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
      {
        _authHelper = OAuth2ClientCredentialsHelper.ForOneDrive(
            tenantId,
            clientId,
            clientSecret
        );
      }
    }

    protected override async Task Authenticate()
    {
      accessToken = await _authHelper.GetAccessTokenAsync();
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
        return await UploadFileWithUploadSession(client, siteUri, file, folderName, fileName, overrideExistingFile);
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

        string encodedFilePath = EscapeGraphPath(filePath);
        var metaUrl = $"https://graph.microsoft.com/v1.0/users/{_user}/drive/root:/SESAR/{encodedFilePath}?$select=@microsoft.graph.downloadUrl";
        using var response = await client.GetAsync(metaUrl);
        if (!response.IsSuccessStatusCode)
        {
          var errorBody = await response.Content.ReadAsStringAsync();
          throw new Exception($"Failed to retrieve file metadata. Status: {(int)response.StatusCode}. Error: {errorBody}");
        }

        var dataDownloadUrlRequestResponse = await response.Content.ReadFromJsonAsync<DownloadUrlRequestResponse>();
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

    /// <summary>
    /// Encode chaque segment d'un chemin Graph separement : les '/' restent des
    /// separateurs. Uri.EscapeDataString sur le chemin complet les transforme en
    /// %2F, ce qui designe un caractere litteral dans un nom de fichier.
    /// </summary>
    private static string EscapeGraphPath(string path)
    {
      return string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
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
          long maxByte = Math.Min(i + Constants.UPLOAD_CHUNK_SIZE - 1, fileBytes.LongLength - 1);
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

    public async override Task<bool> RotateHeaderKey(string key, byte[] oldKek, byte[] newKek)
    {
      if (string.IsNullOrEmpty(key))
        throw new ArgumentNullException(nameof(key));
      if (string.IsNullOrEmpty(accessToken))
        await Authenticate();

      using (var client = new HttpClient())
      {
        client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var splittedPath = key.Split("/");
        string fileName = splittedPath[splittedPath.Length - 1];
        string folderName = String.Join("/", splittedPath[1..^1]);

        string encodedFilePath = EscapeGraphPath(key);
        var metaUrl = $"https://graph.microsoft.com/v1.0/users/{_user}/drive/root:/{encodedFilePath}?$select=@microsoft.graph.downloadUrl";
        using var response = await client.GetAsync(metaUrl);
        if (!response.IsSuccessStatusCode)
        {
          var errorBody = await response.Content.ReadAsStringAsync();
          throw new Exception($"Failed to retrieve file for rotation. Status: {(int)response.StatusCode}. Error: {errorBody}");
        }

        var dataDownloadUrlRequestResponse = await response.Content.ReadFromJsonAsync<DownloadUrlRequestResponse>();
        using (Stream dataStream = await client.GetStreamAsync(dataDownloadUrlRequestResponse.DownloadUrl))
        {
          using (var ms = new MemoryStream())
          {
            await dataStream.CopyToAsync(ms);
            var header = ms.ToArray();

            SEDecryptedSecureFileHeader dHeaderObject = null;

            try
            {
              dHeaderObject = new SEDecryptedSecureFileHeader(oldKek, header);
            }
            catch (InvalidDataException e)
            {
              Console.WriteLine("La rotation a déjà été effectuée");
              return true;
            }
            await UploadFile(dHeaderObject.EncryptHeader(newKek), fileName, folderName);
            return true;
          }
        }
      }
      throw new Exception("Something went wrong while rotating key.");
    }

    public async override Task<byte[]> GetKek(string userKey)
    {
      if (string.IsNullOrEmpty(accessToken))
        await Authenticate();
      if (!Regex.Match(kekPath, ".pem$", RegexOptions.IgnoreCase).Success)
      {
        throw new Exception("Invalid Kek Path");
      }

      using (var client = new HttpClient())
      {
        client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", accessToken);

        string encodedKekPath = EscapeGraphPath(kekPath);
        string kekRequestUrl = $"https://graph.microsoft.com/v1.0/users/{_user}/drive/root:/SESAR/{encodedKekPath}:/content";

        var response = await client.GetAsync(kekRequestUrl);

        // Aucun KEK present : on en genere un, puis on relit. Sans cette relecture
        // le premier upload echouait toujours, meme apres generation reussie.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
          response.Dispose();
          await GenerateAndUploadKek(userKey);
          response = await client.GetAsync(kekRequestUrl);
        }

        using (response)
        {
          if (!response.IsSuccessStatusCode)
          {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to retrieve KEK from OneDrive. Status: {(int)response.StatusCode} {response.StatusCode}. Error: {errorBody}");
          }

          byte[] kek;

          using (Stream dataStream = await response.Content.ReadAsStreamAsync())
          {

            using (var ms = new MemoryStream())
            {
              await dataStream.CopyToAsync(ms);

              AesGcm kekAesGcm = new AesGcm(Convert.FromBase64String(userKey!), Constants.TAG_SIZE_IN_BYTES);
              byte[] encryptedKek = ms.ToArray();
              kek = new byte[encryptedKek.Length - Constants.IV_SIZE_IN_BYTES - Constants.TAG_SIZE_IN_BYTES];

              try
              {
                kekAesGcm.Decrypt(encryptedKek[..Constants.IV_SIZE_IN_BYTES], encryptedKek[Constants.IV_SIZE_IN_BYTES..^Constants.TAG_SIZE_IN_BYTES], encryptedKek[^Constants.TAG_SIZE_IN_BYTES..], kek);
              }
              catch (CryptographicException)
              {
                throw new InvalidParameterException("Invalid user key");
              }
            }
            return kek;
          }
        }
      }
    }

    public async override Task<IntegrationResult> GenerateAndUploadKek(string userKey)
    {
      byte[] newKek = CryptoHelper.GenerateSecureRandomByteArray(Constants.KEY_SIZE_IN_BYTES);

      byte[] encryptedKekFile = SESARCryptoHelper.EncryptBytes(newKek, Convert.FromBase64String(userKey));

      await UploadFile(encryptedKekFile, kekPath.Split("/")[1], kekPath.Split("/")[0]);

      return IntegrationResult.Ok();
    }

    public async override Task<List<string>> GetAllHeadersPaths()
    {
      if (string.IsNullOrEmpty(accessToken))
        await Authenticate();

      List<string> allFiles = new List<string>();

      using (var client = new HttpClient())
      {
        client.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue("Bearer", accessToken);

        async Task SearchAndRotateKeysRecursive(string folderPath = "SESAR")
        {
          string listItemsInFolderRequest = $"https://graph.microsoft.com/v1.0/users/{_user}/drive/root:/{folderPath}:/children";

          while (!string.IsNullOrEmpty(listItemsInFolderRequest))
          {
            var response = await client.GetAsync(listItemsInFolderRequest);
            response.EnsureSuccessStatusCode();

            using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            {
              foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
              {
                var driveItem = new RecursiveSearchedDriveItem(item);

                if (driveItem.IsFolder)
                {
                  await SearchAndRotateKeysRecursive($"{folderPath}/{driveItem.Name}");
                }
                else if (Regex.Match(driveItem.Name, @"\.sech$", RegexOptions.None).Success)
                {
                  allFiles.Add(folderPath + "/" + driveItem.Name);
                }
              }

              listItemsInFolderRequest = doc.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
            }
          }
        }

        await SearchAndRotateKeysRecursive();
      }

      return allFiles;
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

  public class RecursiveSearchedDriveItem
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public bool IsFolder { get; set; }

    public RecursiveSearchedDriveItem(JsonElement item)
    {
      this.Id = item.GetProperty("id").ToString();
      this.Name = item.GetProperty("name").ToString();
      this.IsFolder = item.TryGetProperty("folder", out _);
    }
  }
}
