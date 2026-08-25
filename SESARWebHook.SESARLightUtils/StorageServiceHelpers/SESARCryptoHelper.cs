using SecureExchangesSDK.Helpers;
using SESARLightUtils;
using System.Security.Cryptography;

namespace SESARWebhook.SESARLightUtils.StorageServiceHelpers
{
  public static class SESARCryptoHelper
  {
    public static byte[] EncryptBytes(byte[] file, byte[] key)
    {
      using (var aesGcm = new AesGcm(key, Constants.TAG_SIZE_IN_BYTES))
      {
        byte[] iv = CryptoHelper.GenerateSecureRandomByteArray(Constants.IV_SIZE_IN_BYTES);
        byte[] tag = new byte[Constants.TAG_SIZE_IN_BYTES];
        byte[] encryptedData = new byte[file.Length];
        aesGcm.Encrypt(iv, file, encryptedData, tag);
        return AssembleAesGcmBytes(iv, encryptedData, tag);
      }
    }

    private static byte[] AssembleAesGcmBytes(byte[] nonce, byte[] cypherText, byte[] tag)
    {
      byte[] assembledBytes = new byte[nonce.Length + cypherText.Length + tag.Length];
      Buffer.BlockCopy(nonce, 0, assembledBytes, 0, nonce.Length);
      Buffer.BlockCopy(cypherText, 0, assembledBytes, nonce.Length, cypherText.Length);
      Buffer.BlockCopy(tag, 0, assembledBytes, nonce.Length + cypherText.Length, tag.Length);
      return assembledBytes;
    }

    public static byte[] DecryptBytes(byte[] encryptedData, byte[] key)
    {
      using (var aesGcm = new AesGcm(key, Constants.TAG_SIZE_IN_BYTES))
      {
        byte[] decryptedData = new byte[encryptedData.Length - Constants.IV_SIZE_IN_BYTES - Constants.TAG_SIZE_IN_BYTES];
        aesGcm.Decrypt(encryptedData[..Constants.IV_SIZE_IN_BYTES], encryptedData[Constants.IV_SIZE_IN_BYTES..^Constants.TAG_SIZE_IN_BYTES], encryptedData[^Constants.TAG_SIZE_IN_BYTES..], decryptedData);
        return decryptedData;
      }
    }
  }
}
