using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace SESARWebHook.Core.Auth
{
  public class ApiKeyHelper
  {
    private readonly ApiKeyLocation _location;
    private readonly string _keyName;
    private readonly string _keyValue;
    private readonly string _username;

    private ApiKeyHelper(ApiKeyLocation location, string keyName, string keyValue, string username = null)
    {
      _location = location;
      _keyName = keyName;
      _keyValue = keyValue ?? throw new ArgumentNullException(nameof(keyValue));
      _username = username;
    }

    public static ApiKeyHelper InHeader(string headerName, string apiKey)
    {
      return new ApiKeyHelper(ApiKeyLocation.Header, headerName, apiKey);
    }

    public static ApiKeyHelper AsBearer(string apiKey)
    {
      return new ApiKeyHelper(ApiKeyLocation.Bearer, null, apiKey);
    }

    public static ApiKeyHelper InQueryString(string paramName, string apiKey)
    {
      return new ApiKeyHelper(ApiKeyLocation.QueryString, paramName, apiKey);
    }

    public static ApiKeyHelper AsBasicAuth(string username, string apiKey)
    {
      return new ApiKeyHelper(ApiKeyLocation.BasicAuth, null, apiKey, username);
    }

    public static ApiKeyHelper AsBasicAuthUsername(string apiKey)
    {
      return new ApiKeyHelper(ApiKeyLocation.BasicAuth, null, "", apiKey);
    }

    public void ApplyTo(HttpRequestMessage request)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));

      switch (_location)
      {
        case ApiKeyLocation.Header:
          request.Headers.TryAddWithoutValidation(_keyName, _keyValue);
          break;

        case ApiKeyLocation.Bearer:
          request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _keyValue);
          break;

        case ApiKeyLocation.BasicAuth:
          var credentials = $"{_username ?? ""}:{_keyValue}";
          var base64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));
          request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
          break;

        case ApiKeyLocation.QueryString:
          var uriBuilder = new UriBuilder(request.RequestUri);
          var existingQuery = uriBuilder.Query.TrimStart('?');
          var newParam = $"{Uri.EscapeDataString(_keyName)}={Uri.EscapeDataString(_keyValue)}";
          uriBuilder.Query = string.IsNullOrEmpty(existingQuery)
              ? newParam
              : $"{existingQuery}&{newParam}";
          request.RequestUri = uriBuilder.Uri;
          break;
      }
    }

    public void ApplyTo(HttpClient client)
    {
      if (client == null) throw new ArgumentNullException(nameof(client));

      switch (_location)
      {
        case ApiKeyLocation.Header:
          client.DefaultRequestHeaders.TryAddWithoutValidation(_keyName, _keyValue);
          break;

        case ApiKeyLocation.Bearer:
          client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _keyValue);
          break;

        case ApiKeyLocation.BasicAuth:
          var credentials = $"{_username ?? ""}:{_keyValue}";
          var base64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));
          client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64);
          break;

        case ApiKeyLocation.QueryString:
          throw new InvalidOperationException("Cannot apply query string authentication to HttpClient. Use ApplyTo(HttpRequestMessage) instead.");
      }
    }

    public AuthenticationHeaderValue GetAuthorizationHeader()
    {
      switch (_location)
      {
        case ApiKeyLocation.Bearer:
          return new AuthenticationHeaderValue("Bearer", _keyValue);

        case ApiKeyLocation.BasicAuth:
          var credentials = $"{_username ?? ""}:{_keyValue}";
          var base64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));
          return new AuthenticationHeaderValue("Basic", base64);

        default:
          return null;
      }
    }

    private enum ApiKeyLocation
    {
      Header,
      Bearer,
      QueryString,
      BasicAuth
    }
  }
}
