using Microsoft.VisualStudio.TestTools.UnitTesting;
using SESARLightUtils;
using SESARWebhook.SESARLightUtils.StorageServiceHelpers;
using System;
using System.Collections.Generic;

namespace SESARWebHook.SESARLightUtils.UnitTests.StorageServiceHelpers
{
  [TestClass]
  public class SESARCryptoHelperTests
  {
    [TestMethod]
    public void EncryptBytes_WithValidInputs_ReturnsEncryptedBytes()
    {
      // Arrange
      byte[] fileData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES]; // 32 bytes for AES-256
      
      // Act
      byte[] result = SESARCryptoHelper.EncryptBytes(fileData, key);

      // Assert
      Assert.IsNotNull(result);
      // Expected size: IV (12) + encrypted data (5) + tag (16) = 33
      int expectedSize = Constants.IV_SIZE_IN_BYTES + fileData.Length + Constants.TAG_SIZE_IN_BYTES;
      Assert.AreEqual(expectedSize, result.Length);
    }

    [TestMethod]
    public void EncryptBytes_WithEmptyFile_ReturnsEncryptedBytes()
    {
      // Arrange
      byte[] fileData = new byte[] { };
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] result = SESARCryptoHelper.EncryptBytes(fileData, key);

      // Assert
      Assert.IsNotNull(result);
      // Expected size: IV (12) + encrypted data (0) + tag (16) = 28
      int expectedSize = Constants.IV_SIZE_IN_BYTES + Constants.TAG_SIZE_IN_BYTES;
      Assert.AreEqual(expectedSize, result.Length);
    }

    [TestMethod]
    public void EncryptBytes_WithLargeFile_ReturnsEncryptedBytes()
    {
      // Arrange
      byte[] fileData = new byte[10000];
      for (int i = 0; i < fileData.Length; i++)
      {
        fileData[i] = (byte)(i % 256);
      }
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] result = SESARCryptoHelper.EncryptBytes(fileData, key);

      // Assert
      Assert.IsNotNull(result);
      int expectedSize = Constants.IV_SIZE_IN_BYTES + fileData.Length + Constants.TAG_SIZE_IN_BYTES;
      Assert.AreEqual(expectedSize, result.Length);
    }

    [TestMethod]
    public void EncryptBytes_WithSingleByteFile_ReturnsEncryptedBytes()
    {
      // Arrange
      byte[] fileData = new byte[] { 0xFF };
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] result = SESARCryptoHelper.EncryptBytes(fileData, key);

      // Assert
      Assert.IsNotNull(result);
      int expectedSize = Constants.IV_SIZE_IN_BYTES + 1 + Constants.TAG_SIZE_IN_BYTES;
      Assert.AreEqual(expectedSize, result.Length);
    }

    [TestMethod]
    public void EncryptBytes_DifferentPlaintexts_ProduceDifferentCiphertexts()
    {
      // Arrange
      byte[] plaintext1 = new byte[] { 0x01, 0x02, 0x03 };
      byte[] plaintext2 = new byte[] { 0x04, 0x05, 0x06 };
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] ciphertext1 = SESARCryptoHelper.EncryptBytes(plaintext1, key);
      byte[] ciphertext2 = SESARCryptoHelper.EncryptBytes(plaintext2, key);

      // Assert
      Assert.IsNotNull(ciphertext1);
      Assert.IsNotNull(ciphertext2);
      // The ciphertexts should be different (except both start with the same IV)
      // Compare encrypted data portions (skip IV which is always zeros: indices 12 to 12+3)
      CollectionAssert.AreNotEqual(ciphertext1, ciphertext2);
    }

    [TestMethod]
    public void EncryptBytes_SamePlaintextWithDifferentKeys_ProduceDifferentCiphertexts()
    {
      // Arrange
      byte[] plaintext = new byte[] { 0x01, 0x02, 0x03 };
      byte[] key1 = new byte[Constants.KEY_SIZE_IN_BYTES];
      byte[] key2 = new byte[Constants.KEY_SIZE_IN_BYTES];
      key2[0] = 0xFF; // Make key2 different

      // Act
      byte[] ciphertext1 = SESARCryptoHelper.EncryptBytes(plaintext, key1);
      byte[] ciphertext2 = SESARCryptoHelper.EncryptBytes(plaintext, key2);

      // Assert
      Assert.IsNotNull(ciphertext1);
      Assert.IsNotNull(ciphertext2);
      CollectionAssert.AreNotEqual(ciphertext1, ciphertext2);
    }

    [TestMethod]
    [ExpectedException(typeof(NullReferenceException))]
    public void EncryptBytes_WithNullFile_ThrowsNullReferenceException()
    {
      // Arrange
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      SESARCryptoHelper.EncryptBytes(null!, key);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void EncryptBytes_WithNullKey_ThrowsArgumentNullException()
    {
      // Arrange
      byte[] fileData = new byte[] { 0x01, 0x02, 0x03 };

      // Act
      SESARCryptoHelper.EncryptBytes(fileData, null!);
    }

    [TestMethod]
    public void EncryptBytes_WithMultipleBytesInFile_ContainsIVTagAndEncryptedData()
    {
      // Arrange
      byte[] fileData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] result = SESARCryptoHelper.EncryptBytes(fileData, key);

      // Assert
      Assert.IsNotNull(result);
      // Verify structure: IV (12 bytes) + encrypted data (8 bytes) + tag (16 bytes) = 36 bytes
      Assert.AreEqual(12 + 8 + 16, result.Length);

      // The IV is at the beginning and must be random, not zero-initialized.
      // Reusing a nonce across encryptions under the same key breaks AES-GCM:
      // it leaks the XOR of the plaintexts and allows recovery of the authentication
      // subkey, which lets an attacker forge valid tags.
      byte[] zeroIV = new byte[Constants.IV_SIZE_IN_BYTES];
      byte[] resultIV = new byte[Constants.IV_SIZE_IN_BYTES];
      Array.Copy(result, 0, resultIV, 0, Constants.IV_SIZE_IN_BYTES);
      CollectionAssert.AreNotEqual(zeroIV, resultIV, "The IV must be randomly generated, never zero-filled.");
    }

    [TestMethod]
    public void EncryptBytes_RepeatedCallsWithSameInputs_ProduceDifferentOutput()
    {
      // Arrange
      byte[] fileData = new byte[] { 0x01, 0x02, 0x03 };
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] result1 = SESARCryptoHelper.EncryptBytes(fileData, key);
      byte[] result2 = SESARCryptoHelper.EncryptBytes(fileData, key);

      // Assert
      // Encryption must NOT be deterministic. Identical output for identical input means
      // the nonce is being reused under the same key, which is fatal for AES-GCM.
      Assert.IsNotNull(result1);
      Assert.IsNotNull(result2);
      CollectionAssert.AreNotEqual(result1, result2,
          "Two encryptions of the same plaintext under the same key must differ (unique nonce per call).");
    }

    [TestMethod]
    public void EncryptBytes_WithAllZerosFile_ReturnsEncryptedBytes()
    {
      // Arrange
      byte[] fileData = new byte[100]; // All zeros
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] result = SESARCryptoHelper.EncryptBytes(fileData, key);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual(Constants.IV_SIZE_IN_BYTES + 100 + Constants.TAG_SIZE_IN_BYTES, result.Length);
    }

    [TestMethod]
    public void EncryptBytes_WithAllOnesFile_ReturnsEncryptedBytes()
    {
      // Arrange
      byte[] fileData = new byte[100];
      for (int i = 0; i < fileData.Length; i++)
      {
        fileData[i] = 0xFF;
      }
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] result = SESARCryptoHelper.EncryptBytes(fileData, key);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual(Constants.IV_SIZE_IN_BYTES + 100 + Constants.TAG_SIZE_IN_BYTES, result.Length);
    }

    [TestMethod]
    public void EncryptBytes_WithMaxSizeFile_ReturnsEncryptedBytes()
    {
      // Arrange
      // Create a reasonably large file (1MB)
      byte[] fileData = new byte[1024 * 1024];
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] result = SESARCryptoHelper.EncryptBytes(fileData, key);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual(Constants.IV_SIZE_IN_BYTES + fileData.Length + Constants.TAG_SIZE_IN_BYTES, result.Length);
    }

    [TestMethod]
    public void EncryptBytes_UsesDistinctIVOnEveryCall_Success()
    {
      // Arrange
      byte[] fileData = new byte[] { 0x01, 0x02, 0x03 };
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];
      var seenIVs = new HashSet<string>();

      // Act / Assert
      // Every call must produce a fresh nonce. A collision here would mean the same
      // (key, nonce) pair is used twice, which is exactly the failure mode this
      // helper must never exhibit.
      for (int call = 0; call < 50; call++)
      {
        byte[] result = SESARCryptoHelper.EncryptBytes(fileData, key);
        byte[] iv = new byte[Constants.IV_SIZE_IN_BYTES];
        Array.Copy(result, 0, iv, 0, Constants.IV_SIZE_IN_BYTES);

        Assert.IsTrue(seenIVs.Add(Convert.ToBase64String(iv)),
            $"IV repeated on call {call}: the nonce must be unique for every encryption.");
      }
    }

    [TestMethod]
    public void EncryptBytes_ThenDecryptBytes_RoundTripsWithRandomIV()
    {
      // Arrange
      byte[] fileData = new byte[] { 0x0A, 0x0B, 0x0C, 0x0D, 0x0E };
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] encrypted = SESARCryptoHelper.EncryptBytes(fileData, key);
      byte[] decrypted = SESARCryptoHelper.DecryptBytes(encrypted, key);

      // Assert
      // The IV travels with the ciphertext, so decryption stays agnostic to how it was
      // generated. This is what makes the move to a random IV backward compatible with
      // data encrypted by the previous zero-IV implementation.
      CollectionAssert.AreEqual(fileData, decrypted);
    }
  }
}
