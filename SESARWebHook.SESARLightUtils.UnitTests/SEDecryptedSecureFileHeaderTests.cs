using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SESARLightUtils;

namespace SESARWebHook.SESARLightUtils.UnitTests
{
  [TestClass]
  public class SEDecryptedSecureFileHeaderTests
  {
    // ──────────────────────────────────────────────
    // Constructor 1: Simple initialization
    // ──────────────────────────────────────────────

    [TestMethod]
    public void Constructor_SimpleInitialization_SetsProperties()
    {
      // Arrange
      var dek = new byte[] { 1, 2, 3, 4, 5 };
      var serviceIds = new Dictionary<string, string> { { "id1", "value1" } };
      var trailingIv = new byte[] { 6, 7, 8, 9, 10 };

      // Act
      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Assert
      Assert.IsNotNull(header);
      CollectionAssert.AreEqual(dek, header.Dek);
      Assert.AreEqual(1, header.ServiceIds.Count);
      Assert.AreEqual("value1", header.ServiceIds["id1"]);
      CollectionAssert.AreEqual(trailingIv, header.TrailingIv);
    }

    [TestMethod]
    public void Constructor_WithEmptyCollections_SetsEmptyProperties()
    {
      // Arrange
      var dek = new byte[] { };
      var serviceIds = new Dictionary<string, string>();
      var trailingIv = new byte[] { };

      // Act
      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Assert
      Assert.AreEqual(0, header.Dek.Length);
      Assert.AreEqual(0, header.ServiceIds.Count);
      Assert.AreEqual(0, header.TrailingIv.Length);
    }

    [TestMethod]
    public void Constructor_WithMultipleServiceIds_PreservesAllValues()
    {
      // Arrange
      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string>
      {
        { "id1", "value1" },
        { "id2", "value2" },
        { "id3", "value3" }
      };
      var trailingIv = new byte[] { 10, 11 };

      // Act
      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Assert
      Assert.AreEqual(3, header.ServiceIds.Count);
      Assert.AreEqual("value1", header.ServiceIds["id1"]);
      Assert.AreEqual("value2", header.ServiceIds["id2"]);
      Assert.AreEqual("value3", header.ServiceIds["id3"]);
    }

    [TestMethod]
    public void Constructor_WithLargeDek_SetsLargeDekProperty()
    {
      // Arrange
      var dek = new byte[1000];
      new Random(42).NextBytes(dek);
      var serviceIds = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(43).NextBytes(trailingIv);

      // Act
      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Assert
      Assert.AreEqual(1000, header.Dek.Length);
      CollectionAssert.AreEqual(dek, header.Dek);
    }

    // ──────────────────────────────────────────────
    // Constructor 2: Decrypt from header bytes
    // ──────────────────────────────────────────────

    [TestMethod]
    public void Constructor_WithValidHeader_DecodesSuccessfully()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(100).NextBytes(kek);

      var dek = new byte[] { 1, 2, 3, 4, 5 };
      var serviceIds = new Dictionary<string, string> { { "id1", "value1" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(101).NextBytes(trailingIv);

      // Create a valid header by first creating one and encrypting
      var originalHeader = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);
      byte[] encryptedHeader = originalHeader.EncryptHeader(kek);

      // Act
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);

      // Assert
      Assert.IsNotNull(decodedHeader);
      CollectionAssert.AreEqual(dek, decodedHeader.Dek);
      Assert.AreEqual(1, decodedHeader.ServiceIds.Count);
      Assert.AreEqual("value1", decodedHeader.ServiceIds["id1"]);
      CollectionAssert.AreEqual(trailingIv, decodedHeader.TrailingIv);
    }

    [TestMethod]
    [ExpectedException(typeof(Exception), AllowDerivedTypes = true)]
    public void Constructor_WithInvalidChecksum_ThrowsException()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(200).NextBytes(kek);

      // Create a header with invalid checksum
      byte[] invalidChecksum = new byte[Constants.CHECKSUM_SIZE_IN_BYTES];
      new Random(201).NextBytes(invalidChecksum);

      byte[] trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(202).NextBytes(trailingIv);

      // Assemble header with invalid checksum
      byte[] header = new byte[Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE + invalidChecksum.Length + trailingIv.Length];
      Buffer.BlockCopy(invalidChecksum, 0, header, Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE, invalidChecksum.Length);
      Buffer.BlockCopy(trailingIv, 0, header, Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE + invalidChecksum.Length, trailingIv.Length);

      // Act & Assert - Should throw
      new SEDecryptedSecureFileHeader(kek, header);
    }

    [TestMethod]
    public void Constructor_WithComplexServiceIds_DecodesSuccessfully()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(300).NextBytes(kek);

      var dek = new byte[] { 10, 20, 30 };
      var serviceIds = new Dictionary<string, string>
      {
        { "service1", "id_abc123" },
        { "service2", "id_xyz789" },
        { "service3", "id_def456" }
      };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(301).NextBytes(trailingIv);

      // Create a valid header
      var originalHeader = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);
      byte[] encryptedHeader = originalHeader.EncryptHeader(kek);

      // Act
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);

      // Assert
      Assert.AreEqual(3, decodedHeader.ServiceIds.Count);
      Assert.AreEqual("id_abc123", decodedHeader.ServiceIds["service1"]);
      Assert.AreEqual("id_xyz789", decodedHeader.ServiceIds["service2"]);
      Assert.AreEqual("id_def456", decodedHeader.ServiceIds["service3"]);
      CollectionAssert.AreEqual(dek, decodedHeader.Dek);
    }

    [TestMethod]
    public void Constructor_WithLargeDekInHeader_DecodesSuccessfully()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(400).NextBytes(kek);

      var dek = new byte[5000];
      new Random(401).NextBytes(dek);

      var serviceIds = new Dictionary<string, string> { { "id1", "value1" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(402).NextBytes(trailingIv);

      // Create a valid header
      var originalHeader = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);
      byte[] encryptedHeader = originalHeader.EncryptHeader(kek);

      // Act
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);

      // Assert
      Assert.AreEqual(dek.Length, decodedHeader.Dek.Length);
      CollectionAssert.AreEqual(dek, decodedHeader.Dek);
    }

    [TestMethod]
    public void Constructor_WithEmptyDekInHeader_DecodesSuccessfully()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(500).NextBytes(kek);

      var dek = new byte[] { };
      var serviceIds = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(501).NextBytes(trailingIv);

      var originalHeader = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);
      byte[] encryptedHeader = originalHeader.EncryptHeader(kek);

      // Act
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);

      // Assert
      Assert.AreEqual(0, decodedHeader.Dek.Length);
      Assert.AreEqual("val", decodedHeader.ServiceIds["id"]);
    }

    [TestMethod]
    public void Constructor_WithEmptyServiceIdsInHeader_DecodesSuccessfully()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(600).NextBytes(kek);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string>();
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(601).NextBytes(trailingIv);

      var originalHeader = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);
      byte[] encryptedHeader = originalHeader.EncryptHeader(kek);

      // Act
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);

      // Assert
      Assert.AreEqual(0, decodedHeader.ServiceIds.Count);
      CollectionAssert.AreEqual(dek, decodedHeader.Dek);
    }

    [TestMethod]
    public void Constructor_WithDifferentKek_FailsToDecrypt()
    {
      // Arrange
      var kek1 = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(700).NextBytes(kek1);

      var kek2 = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(701).NextBytes(kek2);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(702).NextBytes(trailingIv);

      var originalHeader = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);
      byte[] encryptedHeader = originalHeader.EncryptHeader(kek1);

      // Act & Assert - Attempting to decrypt with wrong key should throw
      Assert.ThrowsException<Exception>(() => new SEDecryptedSecureFileHeader(kek2, encryptedHeader));
    }

    [TestMethod]
    public void Constructor_ExtractsTrailingIvFromHeader()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(800).NextBytes(kek);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(801).NextBytes(trailingIv);

      var originalHeader = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);
      byte[] encryptedHeader = originalHeader.EncryptHeader(kek);

      // Act
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);

      // Assert - Verify trailing IV matches what was originally set
      CollectionAssert.AreEqual(trailingIv, decodedHeader.TrailingIv);
    }

    // ──────────────────────────────────────────────
    // EncryptHeader Method
    // ──────────────────────────────────────────────

    [TestMethod]
    public void EncryptHeader_WithValidInput_ReturnsEncryptedBytes()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(900).NextBytes(kek);

      var dek = new byte[] { 1, 2, 3, 4, 5 };
      var serviceIds = new Dictionary<string, string> { { "id1", "value1" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(901).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encryptedHeader = header.EncryptHeader(kek);

      // Assert
      Assert.IsNotNull(encryptedHeader);
      Assert.IsTrue(encryptedHeader.Length > 0);
      // Header should contain: size (8) + encrypted content + checksum (12) + IV (12)
      Assert.IsTrue(encryptedHeader.Length >= Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE + Constants.CHECKSUM_SIZE_IN_BYTES + Constants.IV_SIZE_IN_BYTES);
    }

    [TestMethod]
    public void EncryptHeader_RoundTrip_PreservesAllData()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(1000).NextBytes(kek);

      var dek = new byte[] { 5, 4, 3, 2, 1 };
      var serviceIds = new Dictionary<string, string> { { "key", "data" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(1001).NextBytes(trailingIv);

      var originalHeader = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encryptedHeader = originalHeader.EncryptHeader(kek);
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);

      // Assert
      CollectionAssert.AreEqual(dek, decodedHeader.Dek);
      Assert.AreEqual(serviceIds["key"], decodedHeader.ServiceIds["key"]);
      CollectionAssert.AreEqual(trailingIv, decodedHeader.TrailingIv);
    }

    [TestMethod]
    public void EncryptHeader_WithEmptyServiceIds_EncryptsSuccessfully()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(1100).NextBytes(kek);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string>();
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(1101).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encryptedHeader = header.EncryptHeader(kek);

      // Assert
      Assert.IsNotNull(encryptedHeader);
      Assert.IsTrue(encryptedHeader.Length > 0);

      // Verify round-trip
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);
      Assert.AreEqual(0, decodedHeader.ServiceIds.Count);
    }

    [TestMethod]
    public void EncryptHeader_WithLargeDek_EncryptsSuccessfully()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(1200).NextBytes(kek);

      var dek = new byte[5000];
      new Random(1201).NextBytes(dek);

      var serviceIds = new Dictionary<string, string> { { "s1", "v1" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(1202).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encryptedHeader = header.EncryptHeader(kek);

      // Assert
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);
      CollectionAssert.AreEqual(dek, decodedHeader.Dek);
    }

    [TestMethod]
    public void EncryptHeader_WithMultipleServiceIds_EncryptsAndDecryptsCorrectly()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(1300).NextBytes(kek);

      var dek = new byte[] { 7, 8, 9 };
      var serviceIds = new Dictionary<string, string>
      {
        { "service_a", "token_123" },
        { "service_b", "token_456" },
        { "service_c", "token_789" },
        { "service_d", "token_000" }
      };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(1301).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encryptedHeader = header.EncryptHeader(kek);
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);

      // Assert
      Assert.AreEqual(4, decodedHeader.ServiceIds.Count);
      foreach (var kvp in serviceIds)
      {
        Assert.AreEqual(kvp.Value, decodedHeader.ServiceIds[kvp.Key]);
      }
    }

    [TestMethod]
    public void EncryptHeader_WithDifferentKek_ProducesDifferentChecksum()
    {
      // Arrange
      var kek1 = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(1400).NextBytes(kek1);

      var kek2 = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(1401).NextBytes(kek2);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(1402).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encrypted1 = header.EncryptHeader(kek1);
      byte[] encrypted2 = header.EncryptHeader(kek2);

      // Assert - Checksums should differ
      byte[] checksum1 = encrypted1[^(Constants.CHECKSUM_SIZE_IN_BYTES + Constants.IV_SIZE_IN_BYTES)..^Constants.IV_SIZE_IN_BYTES];
      byte[] checksum2 = encrypted2[^(Constants.CHECKSUM_SIZE_IN_BYTES + Constants.IV_SIZE_IN_BYTES)..^Constants.IV_SIZE_IN_BYTES];

      // They should be different
      CollectionAssert.AreNotEqual(checksum1, checksum2);
    }

    [TestMethod]
    public void EncryptHeader_ChecksumIsCorrect()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(1500).NextBytes(kek);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(1501).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encryptedHeader = header.EncryptHeader(kek);

      // Extract and verify checksum
      byte[] checksumFromHeader = encryptedHeader[^(Constants.CHECKSUM_SIZE_IN_BYTES + Constants.IV_SIZE_IN_BYTES)..^Constants.IV_SIZE_IN_BYTES];
      byte[] expectedChecksum;
      using (var sha512 = SHA512.Create())
      {
        expectedChecksum = sha512.ComputeHash(kek)[..Constants.CHECKSUM_SIZE_IN_BYTES];
      }

      // Assert
      CollectionAssert.AreEqual(expectedChecksum, checksumFromHeader);
    }

    [TestMethod]
    public void EncryptHeader_IncludesTrailingIv()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(1600).NextBytes(kek);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(1601).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encryptedHeader = header.EncryptHeader(kek);

      // Assert - Verify the trailing IV is at the end
      byte[] extractedTrailingIv = encryptedHeader[^Constants.IV_SIZE_IN_BYTES..];
      CollectionAssert.AreEqual(trailingIv, extractedTrailingIv);
    }

    [TestMethod]
    public void EncryptHeader_SizeIsEncodedCorrectly()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(1700).NextBytes(kek);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIdsDict = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(1701).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIdsDict, trailingIv);

      // Act
      byte[] encryptedHeader = header.EncryptHeader(kek);

      // Assert - Read the size from the header
      long encodedSize = BinaryPrimitives.ReadInt64LittleEndian(encryptedHeader[..Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE]);

      string serviceIdsJson = "{\"ids\":" + JsonSerializer.Serialize(serviceIdsDict) + "}";
      byte[] serviceIdsBytes = Encoding.UTF8.GetBytes(serviceIdsJson);

      Assert.AreEqual(serviceIdsBytes.Length, encodedSize);
    }

    [TestMethod]
    public void EncryptHeader_ProducesDecryptableOutput()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(1800).NextBytes(kek);

      var dek = new byte[] { 10, 20, 30, 40, 50 };
      var serviceIds = new Dictionary<string, string>
      {
        { "service_x", "value_x" },
        { "service_y", "value_y" }
      };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(1801).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encryptedHeader = header.EncryptHeader(kek);

      // This should not throw
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);

      // Assert
      CollectionAssert.AreEqual(dek, decodedHeader.Dek);
      Assert.AreEqual(serviceIds["service_x"], decodedHeader.ServiceIds["service_x"]);
      Assert.AreEqual(serviceIds["service_y"], decodedHeader.ServiceIds["service_y"]);
    }

    [TestMethod]
    public void EncryptHeader_WithEmptyDek_EncryptsSuccessfully()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(1900).NextBytes(kek);

      var dek = new byte[] { };
      var serviceIds = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(1901).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encryptedHeader = header.EncryptHeader(kek);

      // Assert
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);
      Assert.AreEqual(0, decodedHeader.Dek.Length);
      Assert.AreEqual("val", decodedHeader.ServiceIds["id"]);
    }

    [TestMethod]
    public void EncryptHeader_EncryptedContentCannotBeDecryptedWithWrongKey()
    {
      // Arrange
      var correctKek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(2000).NextBytes(correctKek);

      var wrongKek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(2001).NextBytes(wrongKek);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(2002).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);
      byte[] encryptedHeader = header.EncryptHeader(correctKek);

      // Act & Assert
      Assert.ThrowsException<Exception>(() => new SEDecryptedSecureFileHeader(wrongKek, encryptedHeader));
    }

    [TestMethod]
    public void EncryptHeader_WithSpecialCharactersInServiceIds_EncryptsSuccessfully()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(2100).NextBytes(kek);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string>
      {
        { "service@123", "value#456" },
        { "service-test", "value_test" },
        { "service.prod", "value.data" }
      };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(2101).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encryptedHeader = header.EncryptHeader(kek);
      var decodedHeader = new SEDecryptedSecureFileHeader(kek, encryptedHeader);

      // Assert
      Assert.AreEqual(3, decodedHeader.ServiceIds.Count);
      Assert.AreEqual("value#456", decodedHeader.ServiceIds["service@123"]);
      Assert.AreEqual("value_test", decodedHeader.ServiceIds["service-test"]);
      Assert.AreEqual("value.data", decodedHeader.ServiceIds["service.prod"]);
    }

    [TestMethod]
    public void EncryptHeader_EncryptedBytesAreNotEqual()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(2200).NextBytes(kek);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(2201).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encrypted1 = header.EncryptHeader(kek);
      byte[] encrypted2 = header.EncryptHeader(kek);

      // Assert - Due to random IV in encryption, the encrypted bytes should be different
      // (though they decrypt to the same values)
      CollectionAssert.AreNotEqual(encrypted1, encrypted2);
    }

    [TestMethod]
    public void EncryptHeader_HeaderStructureContainsSizeAtBeginning()
    {
      // Arrange
      var kek = new byte[Constants.KEY_SIZE_IN_BYTES];
      new Random(2300).NextBytes(kek);

      var dek = new byte[] { 1, 2, 3 };
      var serviceIds = new Dictionary<string, string> { { "id", "val" } };
      var trailingIv = new byte[Constants.IV_SIZE_IN_BYTES];
      new Random(2301).NextBytes(trailingIv);

      var header = new SEDecryptedSecureFileHeader(dek, serviceIds, trailingIv);

      // Act
      byte[] encryptedHeader = header.EncryptHeader(kek);

      // Assert - First 8 bytes should be valid size
      long size = BinaryPrimitives.ReadInt64LittleEndian(encryptedHeader[..Constants.HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE]);
      Assert.IsTrue(size > 0);
      Assert.IsTrue(size <= encryptedHeader.Length);
    }
  }
}
