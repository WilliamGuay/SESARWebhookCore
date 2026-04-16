using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SESARWebHook.Core.Auth
{
  /// <summary>
  /// Helper pour l'authentification OAuth 2.0 avec Refresh Token.
  ///
  /// Utilisé pour les APIs qui nécessitent un refresh token initial :
  /// - Zoho CRM, Books, etc.
  /// - Google APIs (Gmail, Drive, etc.)
  /// - Salesforce
  /// - HubSpot
  ///
  /// FONCTIONNALITÉS :
  /// - Obtention automatique du token via refresh token
  /// - Cache du token en mémoire
  /// - Refresh automatique avant expiration
  /// - Thread-safe
  /// - Support pour différents formats de réponse
  ///
  /// EXEMPLE D'UTILISATION :
  /// <code>
  /// var auth = new OAuth2RefreshTokenHelper(
  ///     tokenEndpoint: "https://accounts.zoho.com/oauth/v2/token",
  ///     clientId: "your-client-id",
  ///     clientSecret: "your-client-secret",
  ///     refreshToken: "your-refresh-token"
  /// );
  ///
  /// var token = await auth.GetAccessTokenAsync();
  /// client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
  /// </code>
  /// </summary>
  public class OAuth2RefreshTokenHelper : IDisposable
  {
    private readonly string _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _refreshToken;
    private readonly Dictionary<string, string> _additionalParams;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

    private string _accessToken;
    private DateTime _tokenExpiry;

    /// <summary>
    /// Crée un helper OAuth2 Refresh Token.
    /// </summary>
    /// <param name="tokenEndpoint">URL du endpoint token</param>
    /// <param name="clientId">Client ID de l'application</param>
    /// <param name="clientSecret">Client Secret de l'application</param>
    /// <param name="refreshToken">Refresh token obtenu lors de l'autorisation initiale</param>
    /// <param name="additionalParams">Paramètres additionnels pour la requête token</param>
    /// <param name="httpClient">HttpClient optionnel</param>
    public OAuth2RefreshTokenHelper(
        string tokenEndpoint,
        string clientId,
        string clientSecret,
        string refreshToken,
        Dictionary<string, string> additionalParams = null,
        HttpClient httpClient = null)
    {
      _tokenEndpoint = tokenEndpoint ?? throw new ArgumentNullException(nameof(tokenEndpoint));
      _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
      _clientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
      _refreshToken = refreshToken ?? throw new ArgumentNullException(nameof(refreshToken));
      _additionalParams = additionalParams ?? new Dictionary<string, string>();

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
    /// Crée un helper pour Zoho APIs.
    /// </summary>
    /// <param name="clientId">Client ID Zoho</param>
    /// <param name="clientSecret">Client Secret Zoho</param>
    /// <param name="refreshToken">Refresh Token Zoho</param>
    /// <param name="accountsDomain">Domaine accounts (défaut: https://accounts.zoho.com)</param>
    public static OAuth2RefreshTokenHelper ForZoho(
        string clientId,
        string clientSecret,
        string refreshToken,
        string accountsDomain = "https://accounts.zoho.com")
    {
      var tokenEndpoint = $"{accountsDomain.TrimEnd('/')}/oauth/v2/token";
      return new OAuth2RefreshTokenHelper(tokenEndpoint, clientId, clientSecret, refreshToken);
    }

    /// <summary>
    /// Crée un helper pour Google APIs.
    /// </summary>
    /// <param name="clientId">Client ID Google</param>
    /// <param name="clientSecret">Client Secret Google</param>
    /// <param name="refreshToken">Refresh Token Google</param>
    public static OAuth2RefreshTokenHelper ForGoogle(
        string clientId,
        string clientSecret,
        string refreshToken)
    {
      var tokenEndpoint = "https://oauth2.googleapis.com/token";
      return new OAuth2RefreshTokenHelper(tokenEndpoint, clientId, clientSecret, refreshToken);
    }

    /// <summary>
    /// Crée un helper pour Salesforce.
    /// </summary>
    /// <param name="clientId">Consumer Key Salesforce</param>
    /// <param name="clientSecret">Consumer Secret Salesforce</param>
    /// <param name="refreshToken">Refresh Token Salesforce</param>
    /// <param name="isSandbox">True si environnement sandbox</param>
    public static OAuth2RefreshTokenHelper ForSalesforce(
        string clientId,
        string clientSecret,
        string refreshToken,
        bool isSandbox = false)
    {
      var domain = isSandbox ? "test.salesforce.com" : "login.salesforce.com";
      var tokenEndpoint = $"https://{domain}/services/oauth2/token";
      return new OAuth2RefreshTokenHelper(tokenEndpoint, clientId, clientSecret, refreshToken);
    }

    /// <summary>
    /// Crée un helper pour HubSpot.
    /// </summary>
    /// <param name="clientId">Client ID HubSpot</param>
    /// <param name="clientSecret">Client Secret HubSpot</param>
    /// <param name="refreshToken">Refresh Token HubSpot</param>
    public static OAuth2RefreshTokenHelper ForHubSpot(
        string clientId,
        string clientSecret,
        string refreshToken)
    {
      var tokenEndpoint = "https://api.hubapi.com/oauth/v1/token";
      return new OAuth2RefreshTokenHelper(tokenEndpoint, clientId, clientSecret, refreshToken);
    }

    /// <summary>
    /// Obtient un access token valide.
    /// Le token est mis en cache et rafraîchi automatiquement si expiré.
    /// </summary>
    public async Task<string> GetAccessTokenAsync()
    {
      // Vérifier si le token est encore valide
      if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
      {
        return _accessToken;
      }

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
      var parameters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("client_secret", _clientSecret),
                new KeyValuePair<string, string>("refresh_token", _refreshToken)
            };

      // Ajouter les paramètres additionnels
      foreach (var param in _additionalParams)
      {
        parameters.Add(new KeyValuePair<string, string>(param.Key, param.Value));
      }

      var content = new FormUrlEncodedContent(parameters);
      var response = await _httpClient.PostAsync(_tokenEndpoint, content);
      var responseContent = await response.Content.ReadAsStringAsync();

      if (!response.IsSuccessStatusCode)
      {
        throw new OAuth2Exception(
            $"Failed to refresh access token: {response.StatusCode}",
            responseContent);
      }

      var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseContent);

      // Vérifier les erreurs dans la réponse (certaines APIs retournent 200 avec erreur)
      if (!string.IsNullOrEmpty(tokenResponse.Error))
      {
        throw new OAuth2Exception(
            $"Token error: {tokenResponse.Error}",
            tokenResponse.ErrorDescription ?? responseContent);
      }

      if (string.IsNullOrEmpty(tokenResponse.AccessToken))
      {
        throw new OAuth2Exception(
            "Token response did not contain an access token",
            responseContent);
      }

      _accessToken = tokenResponse.AccessToken;

      // Certaines APIs ne retournent pas expires_in, on assume 1 heure
      var expiresIn = tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600;
      _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

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
    /// Invalide le token en cache.
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
