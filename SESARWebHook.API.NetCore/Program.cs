using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
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
using SESARWebHook.API.Logging;
using SESARWebHook.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace SESARWebHook.API
{
  public class Program
  {
    public static void Main(string[] args)
    {
      StartupConfig startup = null;

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
        var logPath = builder.Configuration["LogPath"];
        if (string.IsNullOrEmpty(logPath))
        {
          logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        }

        builder.Services.AddLogging(config =>
        {
          config.AddConsole();
          config.AddDebug();
          config.SetMinimumLevel(LogLevel.Debug);

          // LogPath etait declare dans appsettings.json et expose par
          // WebHookConfigHelper.LogPath, mais aucun provider ne le consommait.
          // Niveau Information : la progression Debug reste sur la console, le
          // fichier ne retient que ce qui sert au diagnostic post-mortem.
          config.AddProvider(new FileLoggerProvider(logPath, LogLevel.Information));
        });

        // Initialize configuration helper
        WebHookConfigHelper.Initialize(builder.Configuration);

        // Initialize connectors
        startup = new StartupConfig();
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

        // InitializeConnectors() s'execute avant builder.Build(), donc avant l'existence
        // du logger : ses diagnostics sont tamponnes puis rejoues ici. Sans ce rejeu ils
        // n'atteignaient que Console.Error et disparaissaient en hebergement IIS/service.
        foreach (var diagnostic in startup.InitializationDiagnostics)
        {
          logger.Log(diagnostic.Level, "Initialization: {Diagnostic}", diagnostic.Message);
        }

        if (startup.IsInitialized)
        {
          logger.LogInformation("Application initialized successfully");
        }
        else
        {
          logger.LogWarning($"Application initialization warning: {startup.InitializationError}");
        }

        // Filet de sécurité : toute exception non gérée est journalisée côté serveur et
        // convertie en une réponse générique. Sans ce gestionnaire, l'environnement
        // Development afficherait la page d'exception développeur (code source + trace
        // complète) à l'appelant.
        app.UseExceptionHandler(errorApp =>
        {
          errorApp.Run(async ctx =>
          {
            var feature = ctx.Features.Get<IExceptionHandlerPathFeature>();
            var requestId = ctx.TraceIdentifier;

            ctx.RequestServices.GetRequiredService<ILogger<Program>>().LogError(
                feature?.Error,
                "Exception non gérée. RequestId={RequestId} Path={Path}",
                requestId, feature?.Path);

            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            ctx.Response.ContentType = "application/json";

            await ctx.Response.WriteAsJsonAsync(new
            {
              Success = false,
              Message = "Une erreur interne est survenue.",
              RequestId = requestId
            });
          });
        });

        app.UseHttpsRedirection();
        app.MapControllers();

        app.Run();
      }
      catch (Exception ex)
      {
        // Le logger n'existe pas forcement a ce stade (echec avant/pendant Build()) :
        // on vide le tampon sur stderr pour ne pas perdre le diagnostic.
        if (startup != null)
        {
          foreach (var diagnostic in startup.InitializationDiagnostics)
          {
            Console.Error.WriteLine($"[{diagnostic.Level}] {diagnostic.Message}");
          }
        }

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

  /// <summary>
  /// Un message de diagnostic collecte pendant l'initialisation, avec sa severite,
  /// pour etre rejoue dans ILogger une fois celui-ci disponible.
  /// </summary>
  public class InitializationDiagnostic
  {
    public LogLevel Level { get; set; }
    public string Message { get; set; }
  }

  public class StartupConfig
  {
    public ConnectorRegistry ConnectorRegistry { get; private set; }
    public HandlerRegistry HandlerRegistry { get; private set; }
    public GenericConnector GenericConnector { get; private set; }
    public WebhookProcessor WebhookProcessor { get; private set; }
    public bool IsInitialized { get; private set; }
    public string InitializationError { get; private set; }

    /// <summary>
    /// Diagnostics collectes pendant InitializeConnectors(), qui tourne avant que le
    /// logger existe. Rejoues dans ILogger juste apres builder.Build().
    /// </summary>
    public List<InitializationDiagnostic> InitializationDiagnostics { get; } =
        new List<InitializationDiagnostic>();

    private void AddDiagnostic(LogLevel level, string message)
    {
      InitializationDiagnostics.Add(new InitializationDiagnostic
      {
        Level = level,
        Message = message
      });
    }

    public void InitializeConnectors()
    {
      try
      {
        AddDiagnostic(LogLevel.Debug, "Starting connector initialization...");

        ConnectorRegistry = new ConnectorRegistry();
        HandlerRegistry = new HandlerRegistry();

        AddDiagnostic(LogLevel.Debug, "Registering connectors...");

        // Register all available built-in connectors
        ConnectorRegistry.RegisterConnector<FileSystemConnector>();
        ConnectorRegistry.RegisterConnector<ZohoCRMConnector>();
        ConnectorRegistry.RegisterConnector<SharePointConnector>();
        ConnectorRegistry.RegisterConnector<DynamicsConnector>();
        ConnectorRegistry.RegisterConnector<OneDriveConnector>();

        AddDiagnostic(LogLevel.Debug, "Registering generic connector...");

        // Register the Generic Connector (for custom client handlers)
        GenericConnector = new GenericConnector(HandlerRegistry);
        ConnectorRegistry.RegisterConnector("generic", typeof(GenericConnector));
        ConnectorRegistry.PreCacheConnectorInstance("generic", GenericConnector);

        AddDiagnostic(LogLevel.Debug, "Scanning for custom handlers...");

        // Scan for custom handler DLLs in the /Handlers/ folder
        var handlersPath = WebHookConfigHelper.HandlersPath;
        HandlerRegistry.ScanForHandlers(handlersPath);

        AddDiagnostic(LogLevel.Debug, "Initializing connector settings...");

        // Initialize all registered connectors with their settings
        try
        {
          var connectorIds = new[] { "filesystem", "zohocrm", "sharepoint", "dynamics", "onedrive", "generic" };
          foreach (var connectorId in connectorIds)
          {
            try
            {
              AddDiagnostic(LogLevel.Debug, $"Initializing connector: {connectorId}");
              var connectorSettings = GetConnectorSettings(connectorId);
              if (connectorSettings != null && connectorSettings.Count > 0)
              {
                var connectorObj = ConnectorRegistry.GetConnectorInstance(connectorId);
                if (connectorObj is IIntegrationConnector connector)
                {
                  connector.Initialize(connectorSettings);
                  AddDiagnostic(LogLevel.Information, $"Connector '{connectorId}' initialized successfully");
                }
                else
                {
                  AddDiagnostic(LogLevel.Warning, $"Connector '{connectorId}' does not implement IIntegrationConnector");
                }
              }
              else
              {
                AddDiagnostic(LogLevel.Information, $"No settings found for connector '{connectorId}'");
              }
            }
            catch (Exception ex)
            {
              // Exception complete (type, message, inner, trace) : c'est le diagnostic
              // le plus utile quand un connecteur refuse de s'initialiser.
              AddDiagnostic(LogLevel.Error, $"Failed to initialize connector '{connectorId}': {ex}");
            }
          }
        }
        catch (Exception ex)
        {
          AddDiagnostic(LogLevel.Error, $"Error during connector initialization loop: {ex}");
        }

        AddDiagnostic(LogLevel.Debug, "Initializing webhook processor...");

        try
        {
          // Initialize the webhook processor with protected keys
          var key = WebHookConfigHelper.WebHookKey;
          var iv = WebHookConfigHelper.WebHookIV;

          if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(iv))
          {
            WebhookProcessor = new WebhookProcessor(ConnectorRegistry, key, iv);
            IsInitialized = true;
            AddDiagnostic(LogLevel.Information, "Application initialization completed successfully");
          }
          else
          {
            var keyStatus = string.IsNullOrEmpty(key) ? "MISSING" : "OK";
            var ivStatus = string.IsNullOrEmpty(iv) ? "MISSING" : "OK";
            InitializationError = $"WebHook keys not found in connectors.secrets.json. " +
                $"WebHookEncryptionKey={keyStatus}, WebHookEncryptionIV={ivStatus}. " +
                $"Verify that the file exists and contains these keys.";
            IsInitialized = false;
            AddDiagnostic(LogLevel.Error, InitializationError);
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
          AddDiagnostic(LogLevel.Error, InitializationError);
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
        AddDiagnostic(LogLevel.Error, InitializationError);
        throw;
      }
    }

    public System.Collections.Generic.Dictionary<string, string> GetConnectorSettings(string connectorId)
    {
      return WebHookConfigHelper.GetConnectorSettings(connectorId);
    }
  }
}
