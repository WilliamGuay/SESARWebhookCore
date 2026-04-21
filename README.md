# SESAR WebHook Integration Framework

Generic webhook integration framework for [Secure Exchanges (SESAR)](https://www.secure-exchanges.com). Receive encrypted documents from your SESAR vault and route them to any business system: CRM, ERP, SharePoint, file storage, or any custom API.

## How It Works

```
┌───────────────────┐     ┌────────────────────────┐     ┌───────────────────┐
│   SESAR Vault     │────>│    WebHook API         │────>│  Your System      │
│   (Encrypted)     │     │   - Decrypt            │     │  - CRM            │
│                   │     │   - Authenticate       │     │  - SharePoint     │
└───────────────────┘     │   - Route              │     │  - Dynamics 365   │
                          └──────────┬─────────────┘     │  - Custom API     │
                                     │                   └───────────────────┘
                          ┌──────────┴──────────────┐
                          │   Core Library          │
                          │   - OAuth2 Helpers      │
                          │   - API Key Auth        │
                          │   - Data Protection     │
                          │   - Manifest Parsing    │
                          └─────────────────────────┘
```

**What the framework handles:** webhook reception, payload decryption, authentication helpers (OAuth2, API Key, Basic Auth), connector routing, and secrets encryption via Windows DPAPI.

**What you implement:** your business logic in a simple connector class. Extract the data you need from the manifest, call your API, done.

## Quick Start

### Prerequisites

- .NET Core 9.0
- SESAR installed and configured
- Your SESAR webhook encryption keys (Key + IV) and optional PrivateAESKey

### 1. Clone and Build

```powershell
git clone https://github.com/your-org/SESARWebHookCore.git
cd SESARWebHookCore
nuget restore SESARWebHookCore.sln
msbuild SESARWebHookCore.sln /p:Configuration=Release
```

### 2. Configure DPAPI Protection

Edit `Web.config`:

```xml
<!-- DPAPI entropy (Base64) - recommended for additional security -->
<add key="FileEntropy" value="YOUR_BASE64_ENTROPY" />

<!-- Optional: allow all processes on the machine to decrypt (less secure) -->
<!-- <add key="DataProtectionScope" value="LocalMachine" /> -->
```

### 3. Configure Secrets

Copy `connectors.secrets.template.json` to `connectors.secrets.json` and fill in your credentials:

```json
{
  "WebHookEncryptionKey": "your-webhook-key-base64",
  "WebHookEncryptionIV": "your-webhook-iv-base64",
  "PrivateAESKey": "base64Key_base64IV",
  "Connectors": {
    "zoho-crm": {
      "Secrets": {
        "ClientId": "your-client-id",
        "ClientSecret": "your-client-secret",
        "RefreshToken": "your-refresh-token"
      }
    }
  }
}
```

> **Automatic encryption:** On first API call, the secrets file is automatically encrypted with Windows DPAPI (`ProtectedData.Protect`). The plaintext file is replaced by an encrypted binary. By default (`CurrentUser` scope), only the IIS AppPool identity can decrypt it. To update secrets, delete the encrypted file and provide a new plaintext version.

### 4. Configure SESAR

In your SESAR configuration, set the webhook URL:

```
WebHook = https://your-server/api/webhook/filesystem
```

### 5. Test

```bash
curl https://your-server/api/health
# {"Status":"Healthy","Timestamp":"2026-02-17T10:30:00Z"}

curl https://your-server/api/connectors
# Lists all available connectors

curl https://your-server/api/handlers
# Lists all loaded custom handlers
```

## Setup htpps for development

### 1. In an admin powershell terminal, run the following commands

```powershell
$cert = New-SelfSignedCertificate -DnsName "localhost" -CertStoreLocation "Cert:\LocalMachine\My" -FriendlyName "ASP.NET DEV cert" -NotAfter (Get-Date).AddYears(2) -KeyExportPolicy Exportable -KeySpec Signature

$pwd = ConvertTo-SecureString "SECURED_PASSWORD" -AsPlainText -Force

Export-PfxCertificate -Cert $cert -FilePath "PATH_TO_THE_KEY" -Password $pwd
```

### 2. In windows run panel (Win+R), type in mmc and press enter

### 3. In the window that opened, press Ctrl+M

### 4. Press on ``Certificates`` > ``Add >`` > ``My user account`` > ``Finish``

### 5. With the tab on the left of the window, navigate to ``Certificates`` > ``Personal`` > ``Certificates``

### 6. Right click in the window then press ``All Tasks`` then ``Import...``

### 7. In the new window that has opened, press on ``Next`` then enter the path to the key that you created on step 1 then press ``Next``

### 8. Select ``Place all certificates in the following store`` and enter ``Personal`` in the field below the hit ``Next`` then ``Finish``

### 9. In SESARWebHook.API.NetCore/appsettings.json, add the following code:

```json
"Kestrel": {
  "Endpoints": {
    "Https": {
      "Url": "https://localhost:5001"
    }
  }
}
```

## Architecture

### Two Ways to Integrate

**Option A: Built-in Connectors** (ready to use, configure and go)

| Connector | ID | Description |
|-----------|-----|-------------|
| FileSystem | `filesystem` | Writes manifests to disk (debug/test) |
| Zoho CRM | `zoho-crm` | Creates records in Zoho CRM |
| SharePoint | `sharepoint` | Uploads documents to SharePoint Online |
| Dynamics 365 | `dynamics` | Creates entities in Dynamics 365 |

**Option B: Custom Connector** (implement your own logic)

Create a class that extends `IIntegrationConnector`, compile it as a DLL, and drop it in the `/Connectors/` folder. The framework loads it automatically.

### Project Structure

```
SESARWebHookCore/
├── SESARWebHookCore.sln                      # Solution file
├── SESARWebHook.API.NetCore/                 # REST API (entry point)
│   ├── Controllers/
│   │   ├── WebhookController.cs              # Receives webhooks
│   │   ├── ConnectorsController.cs           # Lists/tests connectors
│   │   ├── HandlersController.cs             # Lists/tests handlers
│   │   └── HealthController.cs               # Health check
│   ├── appsettings.json                      # Non-sensitive configuration
│   ├── connectors.secrets.template.json      # Secrets template
|   └── Program.cs                            # Runs the application
├── SESARWebHook.Core.NetCore/                # Core library
│   ├── Auth/                                 # Authentication helpers
│   │   ├── ApiKeyHelper.cs                   # API Key, Bearer, Basic Auth
│   │   ├── OAuth2ClientCredentialsHelper.cs  # Azure AD, SharePoint, Dynamics
│   │   └── OAuth2RefreshTokenHelper.cs       # Zoho, Google, Salesforce, HubSpot
│   ├── Configuration/                        # Config & secrets management
│   │   ├── ConnectorsConfig.cs               # Contains connectors secrets
│   │   ├── IntegrationSettings.cs            # Settings for the integration system
│   │   ├── SecureConfigManager.cs            # DPAPI encryption for secrets
│   │   └── WebHookConfigHelper.cs            # Keys & settings loading
│   ├── Connectors/
│   │   └── GenericConnector.cs               # Routes to custom handlers
│   ├── Interfaces/                           # Extension points
│   │   ├── IConnectorFactory.cs              # Creates connectors
│   │   ├── IIntegrationConnector.cs          # Built-in connector interface
│   │   ├── IWebhookHandler.cs                # Custom handler interface
│   │   └── IWebhookProcessor.cs              # Webhook processing interface
│   ├── Models/
│   │   ├── ConnectorInfo.cs                  # Registered connector information
│   │   ├── IntegrationResult.cs              # Integration operation result
│   │   ├── PushRequest.cs                    # Model for push requests
│   │   ├── PushResponse.cs                   # Model for push requests response
│   │   └── WebhookContext.cs                 # Webhook context information
│   ├── Services/
│   │   ├── ConnectorRegistry.cs              # Connector management
│   │   ├── HandlerRegistry.cs                # Dynamic DLL loading
│   │   └── WebhookProcessor.cs               # Decrypt & route webhooks
│   └── WebhookHandlerBase.cs                 # Base class with helpers
├── SESARWebHook.Connectors.FileSystem/       # Example: File system
├── SESARWebHook.Connectors.ZohoCRM/          # Example: Zoho CRM
├── SESARWebHook.Connectors.SharePoint/       # Example: SharePoint Online
├── SESARWebHook.Connectors.Dynamics/         # Example: Dynamics 365
├── SESARWebHook.Connectors.Template/         # Starter template
└── SESARWebHook.Tests/                       # Unit tests (MSTest)
```

## Create Your Own Connector

### Step 1: Copy Template

Crate a copy of SESARWebHook.Connectors.Template.NetCore

### Step 2: Implement Your Connector

Rename your connector and follow the different sections present in the .cs file inside your connector project in order to have a functioning connector.

## Authentication Helpers

### OAuth2 Client Credentials (Azure AD, SharePoint, Dynamics)

```csharp
// Dynamics 365
var auth = OAuth2ClientCredentialsHelper.ForDynamics365(tenantId, clientId, clientSecret, dynamicsUrl);

// SharePoint Online
var auth = OAuth2ClientCredentialsHelper.ForSharePoint(tenantId, clientId, clientSecret, sharePointUrl);

// Generic Azure AD
var auth = OAuth2ClientCredentialsHelper.ForAzureAD(tenantId, clientId, clientSecret, scope);

var token = await auth.GetAccessTokenAsync(); // Cached + auto-refresh
```

### OAuth2 Refresh Token (Zoho, Google, Salesforce, HubSpot)

```csharp
var auth = OAuth2RefreshTokenHelper.ForZoho(clientId, clientSecret, refreshToken);
var auth = OAuth2RefreshTokenHelper.ForGoogle(clientId, clientSecret, refreshToken);
var auth = OAuth2RefreshTokenHelper.ForSalesforce(clientId, clientSecret, refreshToken);
var auth = OAuth2RefreshTokenHelper.ForHubSpot(clientId, clientSecret, refreshToken);

var token = await auth.GetAccessTokenAsync();
```

### API Key (Header, Bearer, Basic Auth, Query String)

```csharp
var auth = ApiKeyHelper.InHeader("X-API-Key", apiKey);
var auth = ApiKeyHelper.AsBearer(apiKey);
var auth = ApiKeyHelper.AsBasicAuth(username, apiKey);
var auth = ApiKeyHelper.InQueryString("api_key", apiKey);

auth.ApplyTo(httpRequest); // or auth.ApplyTo(httpClient);
```

## WebhookHandlerBase Helpers

```csharp
// Configuration
string GetSetting(string key);                    // Returns null if not found
string GetRequiredSetting(string key);             // Throws if missing
string GetSetting(string key, string defaultVal);  // With default
bool GetSettingBool(string key, bool defaultVal);
int GetSettingInt(string key, int defaultVal);

// Manifest data extraction
string GetSenderEmail(StoreManifest manifest);
string GetSubject(StoreManifest manifest);         // Auto-decodes Base64
int GetFileCount(StoreManifest manifest);
List<string> GetRecipientEmails(StoreManifest manifest);
bool IsReply(StoreManifest manifest);
string GetTrackingId(StoreManifest manifest);
string DecodeBase64(string base64);
```

## API Endpoints

| Method | URL | Description |
|--------|-----|-------------|
| `POST` | `/api/webhook` | Process with default connector |
| `POST` | `/api/webhook/{connectorId}` | Process with specific built-in connector |
| `POST` | `/api/webhook/handler/{handlerId}` | Process with custom handler |
| `POST` | `/api/webhook/multi?connectors=id1,id2` | Process with multiple connectors |
| `POST` | `/api/webhook/handler/multi?handlers=id1,id2` | Process with multiple custom handlers |
| `GET` | `/api/connectors` | List available connectors |
| `GET` | `/api/connectors/{id}` | Get connector details |
| `POST` | `/api/connectors/{id}/test` | Test connector connection |
| `GET` | `/api/handlers` | List loaded custom handlers |
| `GET` | `/api/handlers/{id}` | Get handler details |
| `POST` | `/api/handlers/{id}/test` | Test handler connection |
| `POST` | `/api/handlers/rescan` | Rescan handlers folder for new DLLs |
| `GET` | `/api/health` | Health check |
| `GET` | `/api/health/status` | Detailed status with connector list |

## Security

| Data Type | Location | Protection |
|-----------|----------|------------|
| Non-sensitive settings | `Web.config` | None (standard IIS) |
| All secrets (webhook keys, API keys, tokens) | `connectors.secrets.json` | Windows DPAPI (auto-encrypted) |

**How DPAPI protection works:**

1. You deploy `connectors.secrets.json` as plaintext JSON (starts with `{`)
2. On first access, the API reads the JSON, then immediately encrypts the file with `ProtectedData.Protect(data, entropy, scope)`
3. All subsequent reads detect the encrypted binary, decrypt it with `ProtectedData.Unprotect`, and cache the result in memory
4. The `FileEntropy` (from `Web.config`) adds an additional layer of protection
5. By default (`DataProtectionScope.CurrentUser`), only the IIS AppPool identity can decrypt — copying the file to another machine or user is useless

## Configuration Reference

### Web.config Settings

```xml
<!-- DPAPI entropy for secrets protection (Base64) -->
<add key="FileEntropy" value="BASE64_ENTROPY" />

<!-- Optional: "CurrentUser" (default) or "LocalMachine" for shared access -->
<!-- <add key="DataProtectionScope" value="LocalMachine" /> -->

<!-- Default connector when no ID is specified -->
<add key="DefaultConnectorId" value="filesystem" />

<!-- Optional: custom path for secrets file (default: app directory) -->
<!-- <add key="ConnectorsSecretsPath" value="C:\Config\connectors.secrets.json" /> -->

<!-- Custom handlers folder -->
<add key="HandlersPath" value="~\Handlers\" />

<!-- Enable/disable connectors -->
<add key="Connector:filesystem:Enabled" value="true" />
<add key="Connector:filesystem:OutputPath" value="C:\SESARWebHook\Output\" />
<add key="Connector:zoho-crm:Enabled" value="false" />
<add key="Connector:sharepoint:Enabled" value="false" />
<add key="Connector:dynamics:Enabled" value="false" />

<!-- Enable/disable handlers -->
<add key="Handler:my-crm:Enabled" value="true" />

<!-- Logging -->
<add key="EnableDetailedLogging" value="false" />
```

## Updating

**Your handler (via NuGet):**
```
Update-Package SESARWebHook.Core
```

**The API (via GitHub Release):**
- Download the new release
- Replace files (keep `/Connectors/` and `connectors.secrets.json`)

## Support

- SESAR Documentation: https://help.secure-exchanges.com
- Support: support@secure-exchanges.com

## License

Copyright (c) Secure Exchanges Inc. All rights reserved.
