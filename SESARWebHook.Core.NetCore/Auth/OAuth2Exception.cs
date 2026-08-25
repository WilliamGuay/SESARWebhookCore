using System;

namespace SESARWebHook.Core.Auth
{
  /// <summary>
  /// Exception levée lors d'une erreur d'authentification OAuth2.
  /// </summary>
  public class OAuth2Exception : Exception
  {
    /// <summary>
    /// Réponse brute du serveur d'authentification.
    /// </summary>
    public string RawResponse { get; }

    /// <summary>
    /// Code d'erreur OAuth2 (si disponible).
    /// </summary>
    public string ErrorCode { get; }

    public OAuth2Exception(string message) : base(message)
    {
    }

    public OAuth2Exception(string message, string rawResponse) : base(message)
    {
      RawResponse = rawResponse;
    }

    public OAuth2Exception(string message, string rawResponse, string errorCode) : base(message)
    {
      RawResponse = rawResponse;
      ErrorCode = errorCode;
    }

    public OAuth2Exception(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Représentation destinée EXCLUSIVEMENT à la journalisation côté serveur.
    ///
    /// <see cref="RawResponse"/> contient la réponse brute du serveur d'autorisation, qui
    /// peut inclure des détails de configuration du tenant et des messages d'erreur
    /// internes. Elle est volontairement absente de <see cref="Exception.ToString"/> :
    /// surcharger ToString() ferait fuiter ce contenu par tout chemin appelant ToString()
    /// implicitement — journalisation d'une exception englobante, interpolation de
    /// chaîne, page d'exception développeur.
    ///
    /// N'appeler cette méthode que vers une destination de journalisation.
    /// </summary>
    public string ToDiagnosticString()
    {
      var result = base.ToString();
      if (!string.IsNullOrEmpty(RawResponse))
      {
        result += $"\nRaw Response: {RawResponse}";
      }
      return result;
    }
  }
}
