using Org.BouncyCastle.Security;
using SecureExchangesSDK.Helpers;
using SESARLightUtils;
using SESARWebhook.SESARLightUtils.StorageServiceHelpers;
using SESARWebHook.Core.Auth;
using SESARWebHook.SESARLightUtils.StorageServiceHelpers;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

const int PROGRESS_BAR_WIDTH = 50;

string input = "start";

Console.Write("Veuillez entrer le chemin du fichier de configuration: ");
string confPath = Console.ReadLine();
byte[] jsonConfigsByte = ProtectedData.Unprotect(File.ReadAllBytes(confPath), null, DataProtectionScope.CurrentUser);
string jsonConfigs = System.Text.Encoding.UTF8.GetString(jsonConfigsByte);
JsonDocument SECRETS_ALL_CONNECTORS_DOC = JsonDocument.Parse(jsonConfigs);
JsonElement SECRETS_ALL_CONNECTORS = SECRETS_ALL_CONNECTORS_DOC.RootElement.GetProperty("Connectors");

List<string> connectorOptions = new List<string>();

foreach (var connector in SECRETS_ALL_CONNECTORS.EnumerateObject())
{
  foreach (var property in connector.Value.GetProperty("Secrets").EnumerateObject())
  {
    if (!string.IsNullOrEmpty(property.Value.ToString()))
    {
      connectorOptions.Add(connector.Name);
      break;
    }
  }
}

int selectedOption = 0;
while (selectedOption < 1 || selectedOption > connectorOptions.Count)
{
  Console.WriteLine("Veuillez sélectionner un service parmis les suivants: " + string.Format("{0}", string.Join(", ", connectorOptions.ToArray().Select((x, index) => $"{index + 1}-{x}"))));
  selectedOption = int.Parse(Console.ReadLine()!);
}

Dictionary<string, string> SECRETS = SECRETS_ALL_CONNECTORS.GetProperty(connectorOptions[selectedOption - 1]).GetProperty("Secrets").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString());

var authHelper = OAuth2ClientCredentialsHelper.ForOneDrive(SECRETS["TenantId"], SECRETS["ClientId"], SECRETS["ClientSecret"]);
var accessToken = await authHelper.GetAccessTokenAsync();

using (var client = new HttpClient())
{
  client.DefaultRequestHeaders.Authorization =
      new AuthenticationHeaderValue("Bearer", accessToken);
  client.DefaultRequestHeaders.Accept.Add(
      new MediaTypeWithQualityHeaderValue("application/json"));

  var serviceHelper = new OneDriveServiceHelper(SECRETS);

  Console.Write("Veuillez choisir une option: r-Rotation de clé, g-Génération de clé, q-Quitter: ");
  input = Console.ReadLine()!;

  if (input == "r")
  {
    Console.Write("Veuillez entrer votre clé d'utilisateur: ");
    string userKeyInput = Console.ReadLine()!;

    byte[] newKek;

    if (File.Exists(SECRETS["DefaultFailSafePath"]))
    {
      var newKekEncrypted = File.ReadAllBytes(SECRETS["DefaultFailSafePath"]);
      var userKey = Convert.FromBase64String(userKeyInput);

      var decryptedKek = SESARCryptoHelper.DecryptBytes(newKekEncrypted, userKey);
      newKek = decryptedKek;
      var oldKek = await serviceHelper.GetKek(userKeyInput);

      var headerList = await serviceHelper.GetAllHeadersPaths();

      for (int i = 0; i < headerList.Count; i++)
      {
        var headerPath = headerList[i];
        var rotatedHeader = await serviceHelper.RotateHeaderKey(headerPath, oldKek, decryptedKek);

        DisplayProgressBar(i + 1, headerList.Count);
      }

    }
    else
    {
      newKek = CryptoHelper.GenerateSecureRandomByteArray(32);

      byte[] oldKek;
      try
      {
        oldKek = await serviceHelper.GetKek(SECRETS["UserKey"]);
      }
      catch (InvalidParameterException ex)
      {
        Console.WriteLine("Erreur lors de la récupération de l'ancienne clé KEK: " + ex.Message);
        return;
      }

      var headerList = await serviceHelper.GetAllHeadersPaths();

      byte[] encryptedNewKek = SESARCryptoHelper.EncryptBytes(newKek, Convert.FromBase64String(SECRETS["UserKey"]));

      File.WriteAllBytes(SECRETS["DefaultFailSafePath"], encryptedNewKek);

      for (int i = 0; i < headerList.Count; i++)
      {
        var headerPath = headerList[i];
        var rotatedHeader = await serviceHelper.RotateHeaderKey(headerPath, oldKek, newKek);

        DisplayProgressBar(i + 1, headerList.Count);
      }
    }
    string newUserKey = Convert.ToBase64String(CryptoHelper.GenerateSecureRandomByteArray(32));

    byte[] encryptedKekFile = SESARCryptoHelper.EncryptBytes(newKek, Convert.FromBase64String(newUserKey));

    await serviceHelper.UploadFile(encryptedKekFile, "kek.pem", "utils");

    Console.WriteLine($"Rotation de la clé effectuée. Voici votre nouvelle clé d'utilisateur: {newUserKey}");
    Console.WriteLine("Veuillez la conserver précieusement, elle sera nécessaire pour accéder aux fichiers sécurisés et ne sera pas remontré à nouveau.");
    Console.WriteLine("Vous pouvez également la stocker dans un gestionnaire de mots de passe sécurisé.");
    Console.WriteLine("Pour continuer, appuyez sur une touche.");
    Console.ReadLine();
    File.Delete(SECRETS["DefaultFailSafePath"]);

    string inputEnd = "";

    while (inputEnd != "y")
    {
      Console.WriteLine("Êtes-vous sûr de vouloir continuer? (y/n): ");
      inputEnd = Console.ReadLine();
    }
  }
  else if (input == "g")
  {
    string userKeyInput = "";
    while (userKeyInput == "")
    {
      Console.Write("Veuillez entrer votre clé d'utilisateur: ");
      userKeyInput = Console.ReadLine();

      if (string.IsNullOrEmpty(userKeyInput))
      {
        Console.WriteLine("Le champ ne peux pas être vide");
      }
      else if (userKeyInput != SECRETS_ALL_CONNECTORS_DOC.RootElement.GetProperty("UserKey").ToString())
      {
        Console.WriteLine("La clé d'utilisateur est incorrecte");
        userKeyInput = "";
      }
    }

    serviceHelper.GenerateAndUploadKek(userKeyInput).Wait();
  }
}

static void DisplayProgressBar(long current, long total)
{
  double progress = (double)current / total;
  int filledBars = (int)(progress * PROGRESS_BAR_WIDTH);
  int emptyBars = PROGRESS_BAR_WIDTH - filledBars;
  Console.Write("\r[");
  Console.Write(new string('#', filledBars));
  Console.Write(new string('-', emptyBars));
  Console.Write($"] {progress:P0}");
}

public class DriveItem
{
  public string Id { get; set; }
  public string Name { get; set; }
  public bool IsFolder { get; set; }

  public DriveItem(JsonElement item)
  {
    this.Id = item.GetProperty("id").ToString();
    this.Name = item.GetProperty("name").ToString();
    this.IsFolder = item.TryGetProperty("folder", out _);
  }
}