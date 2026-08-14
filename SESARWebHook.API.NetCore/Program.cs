using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SESARWebHook.Connectors.Dynamics;
using SESARWebHook.Connectors.FileSystem;
using SESARWebHook.Connectors.OneDrive;
using SESARWebHook.Connectors.SharePoint;
using SESARWebHook.Connectors.ZohoCRM;
using SESARWebHook.Core.Configuration;
using SESARWebHook.Core.Connectors;
using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Services;
using SESARWebHook.SESARLightUtils.StorageServiceHelpers;
using System;

namespace SESARWebHook.API
{
  public class Program
  {
    public static void Main(string[] args)
    {
      try
      {
        var builder = WebApplication.CreateBuilder(args);

        // Configure JSON serializer to match SESAR's format
        builder.Services.AddControllers()
            .AddNewtonsoftJson(options =>
            {
              options.SerializerSettings.ContractResolver = new DefaultContractResolver();
              options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
            });

        // Add logging
        builder.Services.AddLogging(config =>
        {
          config.AddConsole();
          config.AddDebug();
          config.SetMinimumLevel(LogLevel.Debug);
        });

        // Initialize configuration helper
        WebHookConfigHelper.Initialize(builder.Configuration);

        // Initialize connectors
        var startup = new StartupConfig();
        startup.InitializeConnectors();

        // Register singletons for DI
        builder.Services.AddSingleton(startup.ConnectorRegistry);
        builder.Services.AddSingleton(startup.HandlerRegistry);
        builder.Services.AddSingleton(startup.GenericConnector);
        if (startup.WebhookProcessor != null)
        {
          builder.Services.AddSingleton(startup.WebhookProcessor);
        }
        builder.Services.AddSingleton(startup);

        var app = builder.Build();

        // Log startup status
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        if (startup.IsInitialized)
        {
          logger.LogInformation("Application initialized successfully");
        }
        else
        {
          logger.LogWarning($"Application initialization warning: {startup.InitializationError}");
        }

        app.UseHttpsRedirection();
        app.MapControllers();

        app.Run();
      }
      catch (Exception ex)
      {
        Console.Error.WriteLine($"Fatal error during application startup: {ex.GetType().Name}");
        Console.Error.WriteLine($"Message: {ex.Message}");
        Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
        if (ex.InnerException != null)
        {
          Console.Error.WriteLine($"Inner Exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
          Console.Error.WriteLine($"Inner Stack Trace: {ex.InnerException.StackTrace}");
        }
        Environment.Exit(1);
      }
    }
  }

  public class StartupConfig
  {
    public ConnectorRegistry ConnectorRegistry { get; private set; }
    public HandlerRegistry HandlerRegistry { get; private set; }
    public GenericConnector GenericConnector { get; private set; }
    public WebhookProcessor WebhookProcessor { get; private set; }
    public bool IsInitialized { get; private set; }
    public string InitializationError { get; private set; }

    public void InitializeConnectors()
    {
      try
      {
        Console.WriteLine("Starting connector initialization...");

        ConnectorRegistry = new ConnectorRegistry();
        HandlerRegistry = new HandlerRegistry();

        Console.WriteLine("Registering connectors...");

        // Register all available built-in connectors
        ConnectorRegistry.RegisterConnector<FileSystemConnector>();
        ConnectorRegistry.RegisterConnector<ZohoCRMConnector>();
        ConnectorRegistry.RegisterConnector<SharePointConnector>();
        ConnectorRegistry.RegisterConnector<DynamicsConnector>();
        ConnectorRegistry.RegisterConnector<OneDriveConnector>();

        Console.WriteLine("Registering generic connector...");

        // Register the Generic Connector (for custom client handlers)
        GenericConnector = new GenericConnector(HandlerRegistry);
        ConnectorRegistry.RegisterConnector("generic", typeof(GenericConnector));
        ConnectorRegistry.PreCacheConnectorInstance("generic", GenericConnector);

        Console.WriteLine("Scanning for custom handlers...");

        // Scan for custom handler DLLs in the /Handlers/ folder
        var handlersPath = WebHookConfigHelper.HandlersPath;
        HandlerRegistry.ScanForHandlers(handlersPath);

        Console.WriteLine("Initializing connector settings...");

        // Initialize all registered connectors with their settings
        try
        {
          var connectorIds = new[] { "filesystem", "zohocrm", "sharepoint", "dynamics", "onedrive", "generic" };
          foreach (var connectorId in connectorIds)
          {
            try
            {
              Console.WriteLine($"  Initializing connector: {connectorId}");
              var connectorSettings = GetConnectorSettings(connectorId);
              if (connectorSettings != null && connectorSettings.Count > 0)
              {
                var connectorObj = ConnectorRegistry.GetConnectorInstance(connectorId);
                if (connectorObj is IIntegrationConnector connector)
                {
                  connector.Initialize(connectorSettings);
                  Console.WriteLine($"  ✓ Connector '{connectorId}' initialized successfully");
                }
                else
                {
                  Console.WriteLine($"  ⚠ Connector '{connectorId}' does not implement IIntegrationConnector");
                }
              }
              else
              {
                Console.WriteLine($"  ℹ No settings found for connector '{connectorId}'");
              }
            }
            catch (Exception ex)
            {
              Console.Error.WriteLine($"  ✗ Failed to initialize connector '{connectorId}': {ex.GetType().Name}: {ex.Message}");
              if (ex.InnerException != null)
              {
                Console.Error.WriteLine($"    Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
              }
            }
          }
        }
        catch (Exception ex)
        {
          Console.Error.WriteLine($"Error during connector initialization loop: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine("Initializing webhook processor...");

        try
        {
          // Initialize the webhook processor with protected keys
          var key = WebHookConfigHelper.WebHookKey;
          var iv = WebHookConfigHelper.WebHookIV;

          if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(iv))
          {
            WebhookProcessor = new WebhookProcessor(ConnectorRegistry, key, iv);
            IsInitialized = true;
            Console.WriteLine("✓ Application initialization completed successfully");
          }
          else
          {
            var keyStatus = string.IsNullOrEmpty(key) ? "MISSING" : "OK";
            var ivStatus = string.IsNullOrEmpty(iv) ? "MISSING" : "OK";
            InitializationError = $"WebHook keys not found in connectors.secrets.json. " +
                $"WebHookEncryptionKey={keyStatus}, WebHookEncryptionIV={ivStatus}. " +
                $"Verify that the file exists and contains these keys.";
            IsInitialized = false;
            Console.Error.WriteLine($"✗ {InitializationError}");
          }
        }
        catch (Exception ex)
        {
          InitializationError = $"Error during secret initialization: {ex.GetType().Name}: {ex.Message}";
          if (ex.InnerException != null)
          {
            InitializationError += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
          }
          IsInitialized = false;
          Console.Error.WriteLine($"✗ {InitializationError}");
        }
      }
      catch (Exception ex)
      {
        InitializationError = $"Fatal error during connector initialization: {ex.GetType().Name}: {ex.Message}";
        if (ex.InnerException != null)
        {
          InitializationError += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
        }
        IsInitialized = false;
        Console.Error.WriteLine($"✗ {InitializationError}");
        throw;
      }
    }

    public System.Collections.Generic.Dictionary<string, string> GetConnectorSettings(string connectorId)
    {
      return WebHookConfigHelper.GetConnectorSettings(connectorId);
    }
  }
}
