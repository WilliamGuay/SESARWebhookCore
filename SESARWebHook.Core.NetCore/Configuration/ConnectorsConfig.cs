using System.Collections.Generic;

namespace SESARWebHook.Core.Configuration
{
  /// <summary>
  /// Configuration des secrets des connecteurs et handlers.
  /// Ce fichier JSON sera automatiquement chiffré avec DPAPI après la première lecture.
  ///
  /// IMPORTANT : Ce fichier contient UNIQUEMENT les secrets (clés webhook, clé AES fichiers,
  /// ApiKey, ClientSecret, etc.)
  /// Les paramètres non-sensibles (Enabled, OutputPath, etc.) restent dans Web.config
  ///
  /// PROTECTION : Au premier démarrage, le fichier JSON en clair est lu, puis immédiatement
  /// chiffré avec ProtectedData.Protect (DPAPI) + FileEntropy. Seul le compte de service
  /// IIS (AppPool identity) peut ensuite le déchiffrer.
  ///
  /// STRUCTURE :
  /// {
  ///   "WebHookEncryptionKey": "base64...",
  ///   "WebHookEncryptionIV": "base64...",
  ///   "PrivateAESKey": "base64Key_base64IV",
  ///   "Connectors": {
  ///     "zoho-crm": { "Secrets": { "ClientId": "...", "ClientSecret": "..." } }
  ///   },
  ///   "Handlers": {
  ///     "my-handler": { "Secrets": { "ApiKey": "..." } }
  ///   }
  /// }
  /// </summary>
  public class ConnectorsSecretsConfig
  {
    /// <summary>
    /// Clé de déchiffrement des webhooks SESAR (Base64).
    /// Fournie par la plateforme Secure Exchanges.
    /// </summary>
    public string WebHookEncryptionKey { get; set; }

    /// <summary>
    /// IV de déchiffrement des webhooks SESAR (Base64).
    /// Fourni par la plateforme Secure Exchanges.
    /// </summary>
    public string WebHookEncryptionIV { get; set; }

    /// <summary>
    /// Clé AES privée pour le déchiffrement des fichiers SESAR.
    /// Format SESAR : "base64Key_base64IV" (clé et IV séparés par un underscore).
    /// Exemple : "dx9u+/nvgmP1niSJdh8Bye3ZNVg+AzenauaDfRHaY+8=_4LnAVgnj/S68tkDKXPmsHQ=="
    ///
    /// Cette clé sert à déchiffrer les fichiers joints dans le manifest SESAR.
    /// </summary>
    public string PrivateAESKey { get; set; }

    /// <summary>
    /// Secrets de chaque connecteur built-in, indexés par ConnectorId
    /// </summary>
    public Dictionary<string, ConnectorSecrets> Connectors { get; set; } = new Dictionary<string, ConnectorSecrets>();

    /// <summary>
    /// Secrets de chaque handler client, indexés par HandlerId
    /// </summary>
    public Dictionary<string, ConnectorSecrets> Handlers { get; set; } = new Dictionary<string, ConnectorSecrets>();

    #region Helpers pour PrivateAESKey

    /// <summary>
    /// Extrait la partie Key du PrivateAESKey (avant le '_')
    /// </summary>
    public string GetFileDecryptionKey()
    {
      if (string.IsNullOrEmpty(PrivateAESKey))
        return null;

      var parts = PrivateAESKey.Split('_');
      return parts.Length >= 1 ? parts[0] : null;
    }

    /// <summary>
    /// Extrait la partie IV du PrivateAESKey (après le '_')
    /// </summary>
    public string GetFileDecryptionIV()
    {
      if (string.IsNullOrEmpty(PrivateAESKey))
        return null;

      var parts = PrivateAESKey.Split('_');
      return parts.Length >= 2 ? parts[1] : null;
    }

    #endregion
  }

  /// <summary>
  /// Secrets d'un connecteur ou handler individuel
  /// </summary>
  public class ConnectorSecrets
  {
    /// <summary>
    /// Paramètres secrets (ClientSecret, ApiKey, RefreshToken, etc.)
    /// </summary>
    public Dictionary<string, string> Secrets { get; set; } = new Dictionary<string, string>();
  }
}
