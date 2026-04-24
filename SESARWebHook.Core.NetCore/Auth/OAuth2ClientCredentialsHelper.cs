using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SESARWebHook.Core.Auth
{
  /// <summary>
  /// Helper pour l'authentification OAuth 2.0 Client Credentials.
  ///
  /// Utilisé pour les APIs qui supportent le flow "client credentials" :
  /// - Microsoft Azure AD (SharePoint, Dynamics 365, Graph API)
  /// - Google Cloud (avec service account)
  /// - AWS Cognito
  /// - Okta
  ///
  /// FONCTIONNALITÉS :
  /// - Obtention automatique du token
  /// - Cache du token en mémoire
  /// - Refresh automatique avant expiration
  /// - Thread-safe
  ///
  /// EXEMPLE D'UTILISATION :
  /// <code>
  /// var auth = new OAuth2ClientCredentialsHelper(
  ///     tokenEndpoint: "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
  ///     clientId: "your-client-id",
  ///     clientSecret: "your-client-secret",
  ///     scope: "https://graph.microsoft.com/.default"
  /// );
  ///
  /// var token = await auth.GetAccessTokenAsync();
  /// client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
  /// </code>
  /// </summary>
  public class OAuth2ClientCredentialsHelper : IDisposable
  {
    private readonly string _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _scope;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

    private string _accessToken;
    private DateTime _tokenExpiry;

    /// <summary>
    /// Crée un helper OAuth2 Client Credentials.
    /// </summary>
    /// <param name="tokenEndpoint">URL du endpoint token (ex: https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token)</param>
    /// <param name="clientId">Client ID de l'application</param>
    /// <param name="clientSecret">Client Secret de l'application</param>
    /// <param name="scope">Scope demandé (ex: https://graph.microsoft.com/.default)</param>
    /// <param name="httpClient">HttpClient optionnel (si null, un nouveau sera créé)</param>
    public OAuth2ClientCredentialsHelper(
        string tokenEndpoint,
        string clientId,
        string clientSecret,
        string scope,
        HttpClient httpClient = null)
    {
      _tokenEndpoint = tokenEndpoint ?? throw new ArgumentNullException(nameof(tokenEndpoint));
      _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
      _clientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
      _scope = scope ?? throw new ArgumentNullException(nameof(scope));

      if (httpClient != null)
      {
        _httpClient = httpClient;
        _ownsHttpClient = false;
      }
      else
      {
        _httpClient = new HttpClient();
        _ownsHttpClient = true;
      }
    }

    /// <summary>
    /// Crée un helper pour Azure AD / Microsoft Identity Platform.
    /// </summary>
    /// <param name="tenantId">Tenant ID Azure</param>
    /// <param name="clientId">Client ID de l'application</param>
    /// <param name="clientSecret">Client Secret</param>
    /// <param name="scope">Scope (ex: https://graph.microsoft.com/.default)</param>
    public static OAuth2ClientCredentialsHelper ForAzureAD(
        string tenantId,
        string clientId,
        string clientSecret,
        string scope)
    {
      var tokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
      return new OAuth2ClientCredentialsHelper(tokenEndpoint, clientId, clientSecret, scope);
    }

    /// <summary>
    /// Crée un helper pour SharePoint Online.
    /// </summary>
    /// <param name="tenantId">Tenant ID Azure</param>
    /// <param name="clientId">Client ID</param>
    /// <param name="clientSecret">Client Secret</param>
    /// <param name="sharePointUrl">URL du site SharePoint (ex: https://contoso.sharepoint.com)</param>
    public static OAuth2ClientCredentialsHelper ForSharePoint(
        string tenantId,
        string clientId,
        string clientSecret,
        string sharePointUrl)
    {
      var uri = new Uri(sharePointUrl);
      var scope = $"https://graph.microsoft.com/.default";
      return ForAzureAD(tenantId, clientId, clientSecret, scope);
    }

    /// <summary>
    /// Crée un helper pour Dynamics 365.
    /// </summary>
    /// <param name="tenantId">Tenant ID Azure</param>
    /// <param name="clientId">Client ID</param>
    /// <param name="clientSecret">Client Secret</param>
    /// <param name="dynamicsUrl">URL Dynamics (ex: https://org.crm.dynamics.com)</param>
    public static OAuth2ClientCredentialsHelper ForDynamics365(
        string tenantId,
        string clientId,
        string clientSecret,
        string dynamicsUrl)
    {
      var scope = $"{dynamicsUrl.TrimEnd('/')}/.default";
      return ForAzureAD(tenantId, clientId, clientSecret, scope);
    }

    /// <summary>
    /// Obtient un access token valide.
    /// Le token est mis en cache et rafraîchi automatiquement si expiré.
    /// </summary>
    public async Task<string> GetAccessTokenAsync()
    {
      // Vérifier si le token est encore valide (avec marge de 60 secondes)
      if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
      {
        return _accessToken;
      }

      // Acquérir le lock pour éviter les requêtes simultanées
      await _tokenLock.WaitAsync();
      try
      {
        // Double-check après avoir acquis le lock
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
        {
          return _accessToken;
        }

        return await RefreshTokenAsync();
      }
      finally
      {
        _tokenLock.Release();
      }
    }

    /// <summary>
    /// Force le rafraîchissement du token.
    /// </summary>
    public async Task<string> RefreshTokenAsync()
    {
      var content = new FormUrlEncodedContent(new[]
      {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("client_secret", _clientSecret),
                new KeyValuePair<string, string>("scope", _scope)
            });

      var response = await _httpClient.PostAsync(_tokenEndpoint, content);
      var responseContent = await response.Content.ReadAsStringAsync();

      if (!response.IsSuccessStatusCode)
      {
        throw new OAuth2Exception(
            $"Failed to obtain access token: {response.StatusCode}",
            responseContent);
      }

      var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseContent);

      if (string.IsNullOrEmpty(tokenResponse.AccessToken))
      {
        throw new OAuth2Exception(
            "Token response did not contain an access token",
            responseContent);
      }

      _accessToken = tokenResponse.AccessToken;
      // Expiration avec marge de sécurité de 60 secondes
      _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);

      return _accessToken;
    }

    /// <summary>
    /// Teste si les credentials sont valides.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
      try
      {
        var token = await GetAccessTokenAsync();
        return !string.IsNullOrEmpty(token);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Invalide le token en cache (force un refresh au prochain appel).
    /// </summary>
    public void InvalidateToken()
    {
      _accessToken = null;
      _tokenExpiry = DateTime.MinValue;
    }

    public void Dispose()
    {
      if (_ownsHttpClient)
      {
        _httpClient?.Dispose();
      }
      _tokenLock?.Dispose();
    }

    private class TokenResponse
    {
      [JsonProperty("access_token")]
      public string AccessToken { get; set; }

      [JsonProperty("expires_in")]
      public int ExpiresIn { get; set; }

      [JsonProperty("token_type")]
      public string TokenType { get; set; }

      [JsonProperty("error")]
      public string Error { get; set; }

      [JsonProperty("error_description")]
      public string ErrorDescription { get; set; }
    }
  }
}
