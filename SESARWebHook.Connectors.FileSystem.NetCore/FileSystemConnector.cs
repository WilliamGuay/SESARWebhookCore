using Newtonsoft.Json;
using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SESARWebHook.Connectors.FileSystem
{
  /// <summary>
  /// Connector that writes SESAR manifests to the local file system.
  /// Useful for debugging, testing, and simple file-based integrations.
  /// </summary>
  public class FileSystemConnector : IIntegrationConnector
  {
    private string _outputPath;
    private bool _createSubfolders;
    private string _fileFormat;

    public string ConnectorId => "filesystem";
    public string DisplayName => "File System";
    public string Description => "Writes SESAR manifests to local file system for debugging or file-based integrations";
    public string Version => "1.0.0";

    public IEnumerable<string> RequiredConfigurationKeys => new[]
    {
            "OutputPath"
        };

    public void Initialize(Dictionary<string, string> settings)
    {
      _outputPath = settings.ContainsKey("OutputPath")
          ? settings["OutputPath"]
          : @"C:\SESARWebHook\Output\";

      _createSubfolders = settings.ContainsKey("CreateSubfolders") &&
                         bool.TryParse(settings["CreateSubfolders"], out var create) && create;

      _fileFormat = settings.ContainsKey("FileFormat")
          ? settings["FileFormat"]
          : "json";
    }

    public Task<bool> ValidateConfigurationAsync(Dictionary<string, string> settings)
    {
      if (!settings.ContainsKey("OutputPath"))
      {
        return Task.FromResult(false);
      }

      var path = settings["OutputPath"];
      if (string.IsNullOrWhiteSpace(path))
      {
        return Task.FromResult(false);
      }

      return Task.FromResult(true);
    }

    public Task<bool> TestConnectionAsync()
    {
      try
      {
        // Ensure the output directory exists
        if (!Directory.Exists(_outputPath))
        {
          Directory.CreateDirectory(_outputPath);
        }

        // Test write access
        var testFile = Path.Combine(_outputPath, ".write_test");
        File.WriteAllText(testFile, "test");
        File.Delete(testFile);

        return Task.FromResult(true);
      }
      catch
      {
        return Task.FromResult(false);
      }
    }

    public async Task<IntegrationResult> ProcessManifestAsync(StoreManifest manifest, WebhookContext context)
    {
      try
      {
        // Ensure output directory exists
        if (!Directory.Exists(_outputPath))
        {
          Directory.CreateDirectory(_outputPath);
        }

        // Determine the output folder
        var outputFolder = _outputPath;
        if (_createSubfolders)
        {
          outputFolder = Path.Combine(_outputPath, DateTime.Now.ToString("yyyy-MM-dd"));
          if (!Directory.Exists(outputFolder))
          {
            Directory.CreateDirectory(outputFolder);
          }
        }

        // Generate filename
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var filename = $"manifest_{timestamp}_{context.RequestId}.{_fileFormat}";
        var filePath = Path.Combine(outputFolder, filename);

        // Serialize and write
        var jsonContent = JsonConvert.SerializeObject(manifest, Formatting.Indented);
        await Task.Run(() => File.WriteAllText(filePath, jsonContent));

        // Also write context info
        var contextFilename = $"context_{timestamp}_{context.RequestId}.json";
        var contextPath = Path.Combine(outputFolder, contextFilename);
        var contextContent = JsonConvert.SerializeObject(new
        {
          context.RequestId,
          context.ReceivedAt,
          context.ConnectorId,
          context.TenantId,
          context.SourceIp
        }, Formatting.Indented);
        await Task.Run(() => File.WriteAllText(contextPath, contextContent));

        return new IntegrationResult
        {
          Success = true,
          Message = $"Manifest written to {filePath}",
          ConnectorId = ConnectorId,
          ExternalReferenceId = filePath,
          ItemsProcessed = 1,
          Metadata = new Dictionary<string, object>
                    {
                        { "FilePath", filePath },
                        { "ContextFilePath", contextPath },
                        { "FileSize", new FileInfo(filePath).Length }
                    }
        };
      }
      catch (Exception ex)
      {
        return IntegrationResult.Fail(
            "Failed to write manifest to file system",
            ex.Message,
            ConnectorId
        );
      }
    }
  }
}
