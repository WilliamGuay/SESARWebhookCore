using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SESARWebHook.Core.Interfaces
{
  /// <summary>
  /// Interface simplifiée pour les clients qui veulent implémenter leur propre webhook handler.
  ///
  /// RESPONSABILITÉS :
  /// - Vous recevez le manifest SESAR déchiffré
  /// - Vous utilisez nos helpers d'authentification (OAuth2, API Key, etc.)
  /// - Vous faites vos propres appels API
  /// - Vous implémentez VOTRE logique d'affaire
  ///
  /// CE QUE NOUS GÉRONS :
  /// - Réception du webhook SESAR
  /// - Déchiffrement du manifest
  /// - Helpers d'authentification (OAuth2ClientCredentialsHelper, OAuth2RefreshTokenHelper, ApiKeyHelper)
  ///
  /// CE QUE VOUS GÉREZ :
  /// - Transformation des données vers votre format
  /// - Appels HTTP vers votre système
  /// - Logique d'affaire spécifique
  ///
  /// EXEMPLE :
  /// <code>
  /// public class MyHandler : WebhookHandlerBase
  /// {
  ///     private OAuth2ClientCredentialsHelper _auth;
  ///
  ///     public override string HandlerId => "my-handler";
  ///     public override string DisplayName => "My Handler";
  ///
  ///     public override void Initialize(Dictionary&lt;string, string&gt; settings)
  ///     {
  ///         base.Initialize(settings);
  ///         _auth = OAuth2ClientCredentialsHelper.ForDynamics365(
  ///             GetRequiredSetting("TenantId"),
  ///             GetRequiredSetting("ClientId"),
  ///             GetRequiredSetting("ClientSecret"),
  ///             GetRequiredSetting("ResourceUrl")
  ///         );
  ///     }
  ///
  ///     public override async Task&lt;IntegrationResult&gt; ProcessAsync(
  ///         StoreManifest manifest,
  ///         WebhookContext context)
  ///     {
  ///         // 1. Extraire les données
  ///         var email = GetSenderEmail(manifest);
  ///         var subject = GetSubject(manifest);
  ///
  ///         // 2. Obtenir le token (avec notre helper)
  ///         var token = await _auth.GetAccessTokenAsync();
  ///
  ///         // 3. Faire votre appel API
  ///         using (var client = new HttpClient())
  ///         {
  ///             client.DefaultRequestHeaders.Authorization =
  ///                 new AuthenticationHeaderValue("Bearer", token);
  ///
  ///             var response = await client.PostAsync("https://your-api.com/endpoint",
  ///                 new StringContent(JsonConvert.SerializeObject(new {
  ///                     email, subject
  ///                 })));
  ///
  ///             if (response.IsSuccessStatusCode)
  ///                 return IntegrationResult.Ok("Success");
  ///             else
  ///                 return IntegrationResult.Fail("Error", await response.Content.ReadAsStringAsync());
  ///         }
  ///     }
  /// }
  /// </code>
  /// </summary>
  public interface IWebhookHandler
  {
    /// <summary>
    /// Identifiant unique de ce handler (ex: "my-company-crm")
    /// Utilisé dans la configuration et le routage.
    /// </summary>
    string HandlerId { get; }

    /// <summary>
    /// Nom affiché dans les logs et interfaces d'admin
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Description de ce que fait ce handler
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Version du handler
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Initialise le handler avec les paramètres de configuration.
    /// Appelé une fois au chargement.
    ///
    /// C'est ici que vous devez créer vos helpers d'authentification.
    /// </summary>
    /// <param name="settings">Paramètres fusionnés de Web.config et connectors.secrets.json</param>
    void Initialize(Dictionary<string, string> settings);

    /// <summary>
    /// Traite un manifest SESAR et fait les actions nécessaires.
    ///
    /// C'est la méthode principale. Vous recevez le manifest déchiffré
    /// et vous faites ce que vous voulez : appels API, transformation, etc.
    /// </summary>
    /// <param name="manifest">Le manifest SESAR déchiffré</param>
    /// <param name="context">Contexte de la requête webhook</param>
    /// <returns>Résultat de l'intégration</returns>
    Task<IntegrationResult> ProcessAsync(StoreManifest manifest, WebhookContext context);

    /// <summary>
    /// Valide que la configuration est correcte.
    /// Appelé au démarrage et lors des tests.
    /// </summary>
    Task<bool> ValidateConfigurationAsync(Dictionary<string, string> settings);

    /// <summary>
    /// Teste la connexion aux systèmes externes.
    /// </summary>
    Task<bool> TestConnectionAsync();
  }
}
