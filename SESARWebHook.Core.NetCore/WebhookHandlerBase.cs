using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SESARWebHook.Core
{
  /// <summary>
  /// Classe de base pour les webhook handlers.
  /// Fournit des helpers pour accéder aux données du manifest et à la configuration.
  ///
  /// UTILISATION :
  /// 1. Héritez de cette classe
  /// 2. Implémentez HandlerId, DisplayName, et ProcessAsync
  /// 3. Utilisez les helpers d'auth dans Core.Auth
  ///
  /// EXEMPLE COMPLET :
  /// <code>
  /// using SESARWebHook.Core;
  /// using SESARWebHook.Core.Auth;
  /// using SESARWebHook.Core.Models;
  ///
  /// public class MyDynamicsHandler : WebhookHandlerBase
  /// {
  ///     private OAuth2ClientCredentialsHelper _auth;
  ///
  ///     public override string HandlerId => "my-dynamics";
  ///     public override string DisplayName => "My Dynamics 365 Handler";
  ///
  ///     public override void Initialize(Dictionary&lt;string, string&gt; settings)
  ///     {
  ///         base.Initialize(settings);
  ///
  ///         // Créer le helper d'auth avec vos paramètres
  ///         _auth = OAuth2ClientCredentialsHelper.ForDynamics365(
  ///             tenantId: GetRequiredSetting("TenantId"),
  ///             clientId: GetRequiredSetting("ClientId"),
  ///             clientSecret: GetRequiredSetting("ClientSecret"),
  ///             dynamicsUrl: GetRequiredSetting("DynamicsUrl")
  ///         );
  ///     }
  ///
  ///     public override async Task&lt;IntegrationResult&gt; ProcessAsync(
  ///         StoreManifest manifest,
  ///         WebhookContext context)
  ///     {
  ///         // Extraire les données avec les helpers
  ///         var email = GetSenderEmail(manifest);
  ///         var subject = GetSubject(manifest);
  ///
  ///         // Obtenir un token (auto-refresh géré par le helper)
  ///         var token = await _auth.GetAccessTokenAsync();
  ///
  ///         // Faire votre appel API
  ///         using (var client = new HttpClient())
  ///         {
  ///             client.DefaultRequestHeaders.Authorization =
  ///                 new AuthenticationHeaderValue("Bearer", token);
  ///
  ///             var payload = new {
  ///                 emailaddress1 = email,
  ///                 subject = subject,
  ///                 description = $"From SESAR: {context.RequestId}"
  ///             };
  ///
  ///             var response = await client.PostAsync(
  ///                 $"{GetRequiredSetting("DynamicsUrl")}/api/data/v9.2/contacts",
  ///                 new StringContent(
  ///                     JsonConvert.SerializeObject(payload),
  ///                     Encoding.UTF8,
  ///                     "application/json"));
  ///
  ///             if (response.IsSuccessStatusCode)
  ///             {
  ///                 return IntegrationResult.Ok("Contact created", HandlerId);
  ///             }
  ///             else
  ///             {
  ///                 var error = await response.Content.ReadAsStringAsync();
  ///                 return IntegrationResult.Fail("API error", error, HandlerId);
  ///             }
  ///         }
  ///     }
  /// }
  /// </code>
  /// </summary>
  public abstract class WebhookHandlerBase : IWebhookHandler
  {
    /// <summary>
    /// Configuration chargée depuis Web.config et connectors.secrets.json
    /// </summary>
    protected Dictionary<string, string> Settings { get; private set; }

    /// <inheritdoc/>
    public abstract string HandlerId { get; }

    /// <inheritdoc/>
    public abstract string DisplayName { get; }

    /// <inheritdoc/>
    public virtual string Description => $"Webhook handler: {DisplayName}";

    /// <inheritdoc/>
    public virtual string Version => "1.0.0";

    /// <inheritdoc/>
    public virtual void Initialize(Dictionary<string, string> settings)
    {
      Settings = settings ?? new Dictionary<string, string>();
    }

    /// <inheritdoc/>
    public abstract Task<IntegrationResult> ProcessAsync(StoreManifest manifest, WebhookContext context);

    /// <inheritdoc/>
    public virtual Task<bool> ValidateConfigurationAsync(Dictionary<string, string> settings)
    {
      return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public virtual Task<bool> TestConnectionAsync()
    {
      return Task.FromResult(true);
    }

    #region Configuration Helpers

    /// <summary>
    /// Récupère un paramètre de configuration.
    /// Retourne null si non trouvé.
    /// </summary>
    protected string GetSetting(string key)
    {
      if (Settings == null) return null;
      return Settings.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Récupère un paramètre de configuration obligatoire.
    /// Lève une exception si non trouvé.
    /// </summary>
    protected string GetRequiredSetting(string key)
    {
      var value = GetSetting(key);
      if (string.IsNullOrEmpty(value))
      {
        throw new InvalidOperationException(
            $"Required setting '{key}' is not configured for handler '{HandlerId}'");
      }
      return value;
    }

    /// <summary>
    /// Récupère un paramètre de configuration avec valeur par défaut.
    /// </summary>
    protected string GetSetting(string key, string defaultValue)
    {
      return GetSetting(key) ?? defaultValue;
    }

    /// <summary>
    /// Récupère un paramètre booléen.
    /// </summary>
    protected bool GetSettingBool(string key, bool defaultValue = false)
    {
      var value = GetSetting(key);
      if (string.IsNullOrEmpty(value)) return defaultValue;
      return bool.TryParse(value, out var result) && result;
    }

    /// <summary>
    /// Récupère un paramètre entier.
    /// </summary>
    protected int GetSettingInt(string key, int defaultValue = 0)
    {
      var value = GetSetting(key);
      if (string.IsNullOrEmpty(value)) return defaultValue;
      return int.TryParse(value, out var result) ? result : defaultValue;
    }

    #endregion

    #region Manifest Data Helpers

    /// <summary>
    /// Décode une chaîne Base64 en texte UTF-8.
    /// </summary>
    protected string DecodeBase64(string base64)
    {
      if (string.IsNullOrEmpty(base64))
        return null;

      try
      {
        var bytes = Convert.FromBase64String(base64);
        return System.Text.Encoding.UTF8.GetString(bytes);
      }
      catch
      {
        return null;
      }
    }

    /// <summary>
    /// Récupère l'email de l'expéditeur depuis le manifest.
    /// </summary>
    protected string GetSenderEmail(StoreManifest manifest)
    {
      return manifest?.OriginalRecipientInfo?.ContactInfo;
    }

    /// <summary>
    /// Récupère le sujet décodé depuis le manifest.
    /// </summary>
    protected string GetSubject(StoreManifest manifest)
    {
      return DecodeBase64(manifest?.Base64Subject);
    }

    /// <summary>
    /// Récupère le nombre de fichiers dans le manifest.
    /// </summary>
    protected int GetFileCount(StoreManifest manifest)
    {
      return manifest?.FilesLocation?.Count ?? 0;
    }

    /// <summary>
    /// Récupère la liste des emails des destinataires.
    /// </summary>
    protected List<string> GetRecipientEmails(StoreManifest manifest)
    {
      var emails = new List<string>();
      if (manifest?.Recipients != null)
      {
        foreach (var recipient in manifest.Recipients)
        {
          if (!string.IsNullOrEmpty(recipient?.Email))
          {
            emails.Add(recipient.Email);
          }
        }
      }
      return emails;
    }

    /// <summary>
    /// Vérifie si c'est une réponse à un échange précédent.
    /// </summary>
    protected bool IsReply(StoreManifest manifest)
    {
      return manifest?.IsReply ?? false;
    }

    /// <summary>
    /// Récupère le premier tracking ID disponible.
    /// </summary>
    protected string GetTrackingId(StoreManifest manifest)
    {
      if (manifest?.TrackingRecipientList?.Count > 0 && manifest.TrackingRecipientList[0] != null)
      {
        return manifest.TrackingRecipientList[0].TrackingID.ToString();
      }
      return null;
    }

    #endregion
  }
}
