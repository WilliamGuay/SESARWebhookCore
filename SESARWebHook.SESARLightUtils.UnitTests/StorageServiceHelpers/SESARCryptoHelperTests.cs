using Microsoft.VisualStudio.TestTools.UnitTesting;
using SESARLightUtils;
using SESARWebhook.SESARLightUtils.StorageServiceHelpers;
using System;

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
      // The IV should be at the beginning (first 12 bytes should all be 0 since we use zero-initialized IV)
      byte[] expectedIV = new byte[Constants.IV_SIZE_IN_BYTES];
      byte[] resultIV = new byte[Constants.IV_SIZE_IN_BYTES];
      Array.Copy(result, 0, resultIV, 0, Constants.IV_SIZE_IN_BYTES);
      CollectionAssert.AreEqual(expectedIV, resultIV);
    }

    [TestMethod]
    public void EncryptBytes_RepeatedCallsWithSameInputs_ProduceSameOutput()
    {
      // Arrange
      byte[] fileData = new byte[] { 0x01, 0x02, 0x03 };
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] result1 = SESARCryptoHelper.EncryptBytes(fileData, key);
      byte[] result2 = SESARCryptoHelper.EncryptBytes(fileData, key);

      // Assert
      Assert.IsNotNull(result1);
      Assert.IsNotNull(result2);
      CollectionAssert.AreEqual(result1, result2);
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
    public void EncryptBytes_OutputStartsWithZeroedIV_Success()
    {
      // Arrange
      byte[] fileData = new byte[] { 0x01, 0x02, 0x03 };
      byte[] key = new byte[Constants.KEY_SIZE_IN_BYTES];

      // Act
      byte[] result = SESARCryptoHelper.EncryptBytes(fileData, key);

      // Assert
      // The IV portion (first 12 bytes) should be all zeros since we initialize with new byte[]
      for (int i = 0; i < Constants.IV_SIZE_IN_BYTES; i++)
      {
        Assert.AreEqual(0, result[i], $"IV byte at index {i} should be 0");
      }
    }
  }
}
