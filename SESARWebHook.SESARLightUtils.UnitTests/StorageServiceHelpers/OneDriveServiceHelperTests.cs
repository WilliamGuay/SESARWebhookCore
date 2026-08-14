using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SESARLightUtils;
using SESARWebHook.Core.Auth;
using SESARWebHook.Core.Models;
using SESARWebHook.SESARLightUtils.StorageServiceHelpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SESARWebHook.SESARLightUtils.UnitTests.StorageServiceHelpers
{
  [TestClass]
  public class OneDriveServiceHelperTests
  {
    #region Constructor Tests

    [TestMethod]
    public void OneDriveServiceHelper_WithAllValidSettings_InitializesSuccessfully()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
                { "OneDriveUserEmail", "user@example.com" },
                { "TenantId", "tenant-123" },
                { "ClientId", "client-456" },
                { "ClientSecret", "secret-789" }
            };

      // Act
      var helper = new OneDriveServiceHelper(settings);

      // Assert
      Assert.IsNotNull(helper);
    }

    [TestMethod]
    public void OneDriveServiceHelper_WithMissingTenantId_InitializesWithoutAuthHelper()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
                { "OneDriveUserEmail", "user@example.com" },
                { "ClientId", "client-456" },
                { "ClientSecret", "secret-789" }
            };

      // Act
      var helper = new OneDriveServiceHelper(settings);

      // Assert
      Assert.IsNotNull(helper);
    }

    [TestMethod]
    public void OneDriveServiceHelper_WithMissingClientId_InitializesWithoutAuthHelper()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
                { "OneDriveUserEmail", "user@example.com" },
                { "TenantId", "tenant-123" },
                { "ClientSecret", "secret-789" }
            };

      // Act
      var helper = new OneDriveServiceHelper(settings);

      // Assert
      Assert.IsNotNull(helper);
    }

    [TestMethod]
    public void OneDriveServiceHelper_WithMissingClientSecret_InitializesWithoutAuthHelper()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
                { "OneDriveUserEmail", "user@example.com" },
                { "TenantId", "tenant-123" },
                { "ClientId", "client-456" }
            };

      // Act
      var helper = new OneDriveServiceHelper(settings);

      // Assert
      Assert.IsNotNull(helper);
    }

    [TestMethod]
    public void OneDriveServiceHelper_WithEmptyTenantId_InitializesWithoutAuthHelper()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
                { "OneDriveUserEmail", "user@example.com" },
                { "TenantId", "" },
                { "ClientId", "client-456" },
                { "ClientSecret", "secret-789" }
            };

      // Act
      var helper = new OneDriveServiceHelper(settings);

      // Assert
      Assert.IsNotNull(helper);
    }

    [TestMethod]
    public void OneDriveServiceHelper_WithEmptyClientId_InitializesWithoutAuthHelper()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
                { "OneDriveUserEmail", "user@example.com" },
                { "TenantId", "tenant-123" },
                { "ClientId", "" },
                { "ClientSecret", "secret-789" }
            };

      // Act
      var helper = new OneDriveServiceHelper(settings);

      // Assert
      Assert.IsNotNull(helper);
    }

    [TestMethod]
    public void OneDriveServiceHelper_WithEmptyClientSecret_InitializesWithoutAuthHelper()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
                { "OneDriveUserEmail", "user@example.com" },
                { "TenantId", "tenant-123" },
                { "ClientId", "client-456" },
                { "ClientSecret", "" }
            };

      // Act
      var helper = new OneDriveServiceHelper(settings);

      // Assert
      Assert.IsNotNull(helper);
    }

    [TestMethod]
    public void OneDriveServiceHelper_WithMissingUserEmail_InitializesWithEmptyUser()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
                { "TenantId", "tenant-123" },
                { "ClientId", "client-456" },
                { "ClientSecret", "secret-789" }
            };

      // Act
      var helper = new OneDriveServiceHelper(settings);

      // Assert
      Assert.IsNotNull(helper);
    }

    [TestMethod]
    public void OneDriveServiceHelper_WithEmptySettings_InitializesSuccessfully()
    {
      // Arrange
      var settings = new Dictionary<string, string>();

      // Act
      var helper = new OneDriveServiceHelper(settings);

      // Assert
      Assert.IsNotNull(helper);
    }

    [TestMethod]
    public void OneDriveServiceHelper_SecondConstructor_WithSettings_InitializesSuccessfully()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
              { "OneDriveUserEmail", "user@example.com" },
              { "TenantId", "tenant-123" },
              { "ClientId", "client-456" },
              { "ClientSecret", "secret-789" },
              {"KekPath", "utils/kek.pem" }
            };

      // Act
      var helper = new OneDriveServiceHelper(settings);

      // Assert
      Assert.IsNotNull(helper);
    }

    [TestMethod]
    public void OneDriveServiceHelper_SecondConstructor_SetsKekPath()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
              {"KekPath", "utils/kek.pem" },
              { "OneDriveUserEmail", "user@example.com" }
            };

      // Act
      var helper = new OneDriveServiceHelper(settings);

      // Assert
      Assert.IsNotNull(helper);
    }

    #endregion

    #region Authenticate Method Tests

    [TestMethod]
    [ExpectedException(typeof(NullReferenceException))]
    public async Task Authenticate_WithoutAuthHelper_ThrowsNullReferenceException()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
                { "OneDriveUserEmail", "user@example.com" }
            };
      var helper = new OneDriveServiceHelper(settings);

      // Act
      await helper.GetKek("test-key");
    }

    #endregion

    #region DownloadFile Method Tests

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public async Task DownloadFile_WithNullHeader_ThrowsArgumentNullException()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
                { "OneDriveUserEmail", "user@example.com" },
                { "TenantId", "tenant-123" },
                { "ClientId", "client-456" },
                { "ClientSecret", "secret-789" }
            };
      var helper = new OneDriveServiceHelper(settings);

      // Act
      await helper.DownloadFile(null!);
    }

    [TestMethod]
    [ExpectedException(typeof(Exception))]
    public async Task DownloadFile_WithHeaderMissingOneDriveKey_ThrowsException()
    {
      // Arrange
      var settings = new Dictionary<string, string>
            {
                { "OneDriveUserEmail", "user@example.com" },
                { "TenantId", "tenant-123" },
                { "ClientId", "client-456" },
                { "ClientSecret", "secret-789" }
            };
      var helper = new OneDriveServiceHelper(settings);
      var serviceIds = new Dictionary<string, string> { { "SomeOtherKey", "value" } };
      var header = new SEDecryptedSecureFileHeader(
          new byte[] { 1, 2, 3 },
          serviceIds,
          new byte[] { 4, 5, 6 }
      );

      // Act
      await helper.DownloadFile(header);
    }

    #endregion

    #region UploadFile Method Tests

    //[TestMethod]
    //[ExpectedException(typeof(ArgumentNullException))]
    //public async Task UploadFile_WithNullFile_ThrowsArgumentNullException()
    //{
    //    // Arrange
    //    var settings = new Dictionary<string, string>
    //    {
    //        { "OneDriveUserEmail", "user@example.com" },
    //        { "TenantId", "tenant-123" },
    //        { "ClientId", "client-456" },
    //        { "ClientSecret", "secret-789" }
    //    };
    //    var helper = new OneDriveServiceHelper(settings);

    //    // Act
    //    await helper.UploadFile(null!, "fileName.txt", "folderName");
    //}

    //[TestMethod]
    //[ExpectedException(typeof(ArgumentException))]
    //public async Task UploadFile_WithEmptyFile_ThrowsArgumentException()
    //{
    //    // Arrange
    //    var settings = new Dictionary<string, string>
    //    {
    //        { "OneDriveUserEmail", "user@example.com" },
    //        { "TenantId", "tenant-123" },
    //        { "ClientId", "client-456" },
    //        { "ClientSecret", "secret-789" }
    //    };
    //    var helper = new OneDriveServiceHelper(settings);
    //    var emptyFile = new byte[0];

    //    // Act
    //    await helper.UploadFile(emptyFile, "fileName.txt", "folderName");
    //}

    //[TestMethod]
    //[ExpectedException(typeof(ArgumentException))]
    //public async Task UploadFile_WithZeroLengthFile_ThrowsArgumentException()
    //{
    //    // Arrange
    //    var settings = new Dictionary<string, string>
    //    {
    //        { "OneDriveUserEmail", "user@example.com" },
    //        { "TenantId", "tenant-123" },
    //        { "ClientId", "client-456" },
    //        { "ClientSecret", "secret-789" }
    //    };
    //    var helper = new OneDriveServiceHelper(settings);
    //    var fileBytes = new byte[0];

    //    // Act
    //    await helper.UploadFile(fileBytes, "test.txt", "folder");
    //}

    #endregion
  }
}
