using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;

namespace SESARWebHook.Core.Configuration
{
  public static class WebHookConfigHelper
  {
    private static IConfiguration _configuration;
    private static Dictionary<string, Dictionary<string, string>> _connectorSettingsCache = new Dictionary<string, Dictionary<string, string>>();

    public static void Initialize(IConfiguration configuration)
    {
      _configuration = configuration;

      // Initialize SecureConfigManager with values from appsettings.json
      SecureConfigManager.Initialize(
          connectorsSecretsPath: _configuration["ConnectorsSecretsPath"],
          dataProtectionScope: _configuration["DataProtectionScope"],
          fileEntropy: _configuration["FileEntropy"],
          appBasePath: AppDomain.CurrentDomain.BaseDirectory
      );
    }

    #region Clés SESAR (depuis connectors.secrets.json protégé par DPAPI)

    public static string WebHookKey
    {
      get
      {
        return SecureConfigManager.GetWebHookEncryptionKey();
      }
    }

    public static string WebHookIV
    {
      get
      {
        return SecureConfigManager.GetWebHookEncryptionIV();
      }
    }

    #endregion

    #region Configuration générale

    public static string FileEntropy
    {
      get
      {
        return _configuration?["FileEntropy"];
      }
    }

    public static string DefaultConnectorId
    {
      get
      {
        return _configuration?["DefaultConnectorId"] ?? "filesystem";
      }
    }

    public static bool EnableDetailedLogging
    {
      get
      {
        var value = _configuration?["EnableDetailedLogging"];
        return !string.IsNullOrEmpty(value) && bool.TryParse(value, out var result) && result;
      }
    }

    public static string LogPath
    {
      get
      {
        return _configuration?["LogPath"] ?? @"C:\SESARWebHook\Logs\";
      }
    }

    public static string HandlersPath
    {
      get
      {
        var customPath = _configuration?["HandlersPath"];
        if (!string.IsNullOrEmpty(customPath))
          return customPath;

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Handlers");
      }
    }

    #endregion

    #region Configuration des connecteurs

    public static bool IsConnectorEnabled(string connectorId)
    {
      var enabledSetting = _configuration?[$"Connector:{connectorId}:Enabled"];
      return !string.IsNullOrEmpty(enabledSetting) &&
             bool.TryParse(enabledSetting, out var enabled) && enabled;
    }

    public static Dictionary<string, string> GetConnectorSettings(string connectorId)
    {
      if (_connectorSettingsCache.ContainsKey(connectorId))
      {
        return _connectorSettingsCache[connectorId];
      }

      var settings = new Dictionary<string, string>();

      // 1. Lire les paramètres non-sensibles depuis appsettings.json
      var section = _configuration?.GetSection($"Connector:{connectorId}");
      if (section != null)
      {
        foreach (var child in section.GetChildren())
        {
          settings[child.Key] = child.Value;
        }
      }

      // 2. Fusionner avec les secrets du fichier JSON chiffré
      var secrets = SecureConfigManager.GetConnectorSecrets(connectorId);
      foreach (var secret in secrets)
      {
        settings[secret.Key] = secret.Value;
      }

      _connectorSettingsCache[connectorId] = settings;
      return settings;
    }

    #endregion

    #region Configuration des handlers

    public static bool IsHandlerEnabled(string handlerId)
    {
      var enabledSetting = _configuration?[$"Handler:{handlerId}:Enabled"];
      return !string.IsNullOrEmpty(enabledSetting) &&
             bool.TryParse(enabledSetting, out var enabled) && enabled;
    }

    public static Dictionary<string, string> GetHandlerSettings(string handlerId)
    {
      var settings = new Dictionary<string, string>();

      var section = _configuration?.GetSection($"Handler:{handlerId}");
      if (section != null)
      {
        foreach (var child in section.GetChildren())
        {
          settings[child.Key] = child.Value;
        }
      }

      var secrets = SecureConfigManager.GetHandlerSecrets(handlerId);
      foreach (var secret in secrets)
      {
        settings[secret.Key] = secret.Value;
      }

      return settings;
    }

    #endregion

    #region Cache

    public static void ClearCache()
    {
      _connectorSettingsCache.Clear();
      SecureConfigManager.ReloadSecrets();
    }

    #endregion
  }
}
