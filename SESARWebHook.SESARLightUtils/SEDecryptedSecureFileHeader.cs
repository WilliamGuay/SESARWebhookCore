using SESARWebhook.SESARLightUtils.StorageServiceHelpers;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SESARLightUtils
{
  public class SEDecryptedSecureFileHeader
  {
    public byte[] Dek { get; }
    public Dictionary<string, string> ServiceIds { get; }
    public byte[] TrailingIv { get; }

    public SEDecryptedSecureFileHeader(byte[] dek, Dictionary<string, string> serviceIds, byte[] trailingIv)
    {
      Dek = dek;
      ServiceIds = serviceIds;
      TrailingIv = trailingIv;
    }

    public SEDecryptedSecureFileHeader(byte[] kek, byte[] header)
    {

      byte[] checksum = header[^(Constants.CHECKSUM_SIZE_IN_BYTES + Constants.IV_SIZE_IN_BYTES)..^Constants.IV_SIZE_IN_BYTES];
      using (var sha512 = SHA512.Create())
      {
        if (!checksum.SequenceEqual(sha512.ComputeHash(kek)[..Constants.CHECKSUM_SIZE_IN_BYTES]))
        {
          throw new Exception("Invalid key encryption key");
        }
      }

      TrailingIv = header[^Constants.IV_SIZE_IN_BYTES..];
      long serviceIdsSizeInBytes = BinaryPrimitives.ReadInt64LittleEndian(header[..Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE]);

      byte[] encryptedHeaderContent = header[Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE..^(Constants.CHECKSUM_SIZE_IN_BYTES + Constants.IV_SIZE_IN_BYTES)];
      var aesGcm = new AesGcm(kek, Constants.TAG_SIZE_IN_BYTES);
      byte[] decryptedData = new byte[encryptedHeaderContent[Constants.IV_SIZE_IN_BYTES..^Constants.TAG_SIZE_IN_BYTES].LongLength];
      aesGcm.Decrypt(encryptedHeaderContent[..Constants.IV_SIZE_IN_BYTES], encryptedHeaderContent[Constants.IV_SIZE_IN_BYTES..^Constants.TAG_SIZE_IN_BYTES], encryptedHeaderContent[^Constants.TAG_SIZE_IN_BYTES..], decryptedData);
      Dek = decryptedData[..^(int)serviceIdsSizeInBytes];

      var ids = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(Encoding.UTF8.GetString(decryptedData[^(int)serviceIdsSizeInBytes..]))!;
      ServiceIds = ids["ids"];
    }

    public byte[] EncryptHeader(byte[] kek)
    {
      string serviceIdsJson = "{\"ids\":" + JsonSerializer.Serialize(ServiceIds) + "}";
      byte[] serviceIdsBytes = Encoding.UTF8.GetBytes(serviceIdsJson);

      byte[] headerContent = new byte[serviceIdsBytes.Length + Dek.Length];
      Buffer.BlockCopy(Dek, 0, headerContent, 0, Dek.Length);
      Buffer.BlockCopy(serviceIdsBytes, 0, headerContent, Dek.Length, serviceIdsBytes.Length);

      byte[] encryptedHeaderContent = SESARCryptoHelper.EncryptBytes(headerContent, kek);
      byte[] sizeBytes = new byte[8];
      BinaryPrimitives.WriteInt64LittleEndian(sizeBytes, serviceIdsBytes.Length);

      byte[] checksum = new byte[12];
      using (var sha =  SHA512.Create())
      {
        checksum = sha.ComputeHash(kek)[..12];
      }

      byte[] header = new byte[sizeBytes.Length + encryptedHeaderContent.Length + checksum.Length + TrailingIv.Length];
      Buffer.BlockCopy(sizeBytes, 0, header, 0, sizeBytes.Length);
      Buffer.BlockCopy(encryptedHeaderContent, 0, header, sizeBytes.Length, encryptedHeaderContent.Length);
      Buffer.BlockCopy(checksum, 0, header, sizeBytes.Length + encryptedHeaderContent.Length, checksum.Length);
      Buffer.BlockCopy(TrailingIv, 0, header, sizeBytes.Length + encryptedHeaderContent.Length + checksum.Length, TrailingIv.Length);

      return header;
    }
  }
}
