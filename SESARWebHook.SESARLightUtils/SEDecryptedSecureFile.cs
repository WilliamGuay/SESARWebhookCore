using System.Security.Cryptography;

namespace SESARLightUtils
{
  public class SEDecryptedSecureFile
  {
    public byte[] Data { get; }
    
    public SEDecryptedSecureFile(byte[] data, SEDecryptedSecureFileHeader header)
    {
      var aesGcm = new AesGcm(header.Dek, Constants.TAG_SIZE_IN_BYTES);

      byte[] fullData = new byte[data.Length + header.TrailingIv.Length];
      Buffer.BlockCopy(header.TrailingIv, 0, fullData, 0, header.TrailingIv.Length);
      Buffer.BlockCopy(data, 0, fullData, header.TrailingIv.Length, data.Length);

      byte[] decryptedBytes = new byte[fullData.Length - Constants.TAG_SIZE_IN_BYTES - Constants.IV_SIZE_IN_BYTES];
      aesGcm.Decrypt(fullData[..Constants.IV_SIZE_IN_BYTES], fullData[Constants.IV_SIZE_IN_BYTES..^Constants.TAG_SIZE_IN_BYTES], fullData[^Constants.TAG_SIZE_IN_BYTES..], decryptedBytes);
      Data = decryptedBytes;
    }

    public void SaveFile(string path)
    {
      using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write))
      {
        fileStream.Write(Data);
      }
    }
  }
}
