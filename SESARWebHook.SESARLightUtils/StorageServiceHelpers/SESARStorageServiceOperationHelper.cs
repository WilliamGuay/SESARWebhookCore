using SESARWebHook.Core.Models;

namespace SESARLightUtils.StorageServiceHelpers
{
  public abstract class SESARStorageServicesOperationHelper
  {
    protected string kekPath;
    public abstract Task<string> UploadFile(byte[] file, string fileName, string folderName, bool overrideExistingFile = true);

    /**
     * <summary>
     * Method <c>Create</c> SEDecryptedSecureFile
     * </summary>
     */
    public abstract Task<SEDecryptedSecureFile> DownloadFile(SEDecryptedSecureFileHeader header);
    public abstract Task<bool> RotateHeaderKey(string filePath, byte[] oldKek, byte[] newKek);
    public abstract Task<byte[]> GetKek(string userKey);
    public abstract Task<IntegrationResult> GenerateAndUploadKek(string userKey);
    public abstract Task<List<string>> GetAllHeadersPaths();

    protected abstract Task Authenticate();

    protected async Task<byte[]> RotateFileKek(byte[] header, string newKekPath)
    {
      byte[] kek = File.ReadAllBytes(kekPath);
      SEDecryptedSecureFileHeader dHeader = new SEDecryptedSecureFileHeader(kek, header);
      byte[] newKek = File.ReadAllBytes(newKekPath);
      return dHeader.EncryptHeader(newKek);
    }
  }
}
