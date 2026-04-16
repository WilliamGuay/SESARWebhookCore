using Newtonsoft.Json;
using System.Collections.Generic;

namespace SESARWebHook.Connectors.ZohoCRM.Models
{
  /// <summary>
  /// Zoho OAuth token response
  /// </summary>
  public class ZohoTokenResponse
  {
    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    [JsonProperty("refresh_token")]
    public string RefreshToken { get; set; }

    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonProperty("token_type")]
    public string TokenType { get; set; }

    [JsonProperty("error")]
    public string Error { get; set; }
  }

  /// <summary>
  /// Zoho CRM API response wrapper
  /// </summary>
  public class ZohoApiResponse<T>
  {
    [JsonProperty("data")]
    public List<T> Data { get; set; }

    [JsonProperty("info")]
    public ZohoResponseInfo Info { get; set; }
  }

  /// <summary>
  /// Zoho response info
  /// </summary>
  public class ZohoResponseInfo
  {
    [JsonProperty("per_page")]
    public int PerPage { get; set; }

    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("page")]
    public int Page { get; set; }

    [JsonProperty("more_records")]
    public bool MoreRecords { get; set; }
  }

  /// <summary>
  /// Zoho CRM record creation response
  /// </summary>
  public class ZohoCreateResponse
  {
    [JsonProperty("data")]
    public List<ZohoRecordResult> Data { get; set; }
  }

  /// <summary>
  /// Result of a record operation
  /// </summary>
  public class ZohoRecordResult
  {
    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("details")]
    public ZohoRecordDetails Details { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }
  }

  /// <summary>
  /// Record details after creation
  /// </summary>
  public class ZohoRecordDetails
  {
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("Modified_Time")]
    public string ModifiedTime { get; set; }

    [JsonProperty("Created_Time")]
    public string CreatedTime { get; set; }
  }

  /// <summary>
  /// Generic Zoho CRM record
  /// </summary>
  public class ZohoRecord
  {
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object> Fields { get; set; }

    public ZohoRecord()
    {
      Fields = new Dictionary<string, object>();
    }
  }
}
