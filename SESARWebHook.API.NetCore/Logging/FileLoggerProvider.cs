using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;

namespace SESARWebHook.API.Logging
{
  /// <summary>
  /// Provider de journalisation fichier minimal, sans dépendance externe : un fichier
  /// par jour dans le répertoire configuré par LogPath.
  ///
  /// Raison d'être : l'application n'enregistrait que les providers Console et Debug.
  /// En exécution console le diagnostic de démarrage était visible, mais sous IIS ou
  /// en service Windows le stdout n'est pas capturé par défaut et tout le détail des
  /// échecs d'initialisation était perdu — seul le booléen HasInitializationError
  /// de /api/health/status subsistait, ce qui ne permet pas de corriger quoi que ce soit.
  /// </summary>
  public sealed class FileLoggerProvider : ILoggerProvider
  {
    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly object _sync = new object();
    private bool _writeFailureReported;

    public FileLoggerProvider(string directory, LogLevel minLevel)
    {
      _directory = directory;
      _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName)
    {
      return new FileLogger(this, categoryName);
    }

    internal bool IsEnabled(LogLevel level)
    {
      return level != LogLevel.None && level >= _minLevel;
    }

    internal void Write(string line)
    {
      lock (_sync)
      {
        try
        {
          Directory.CreateDirectory(_directory);
          var path = Path.Combine(_directory, $"webhook-{DateTime.UtcNow:yyyyMMdd}.log");
          File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
          // Un disque plein, un chemin invalide ou des droits manquants ne doivent
          // jamais faire tomber l'API. On signale une seule fois sur stderr, puis on
          // continue sans journalisation fichier.
          if (!_writeFailureReported)
          {
            _writeFailureReported = true;
            Console.Error.WriteLine(
                $"[FileLogger] Journalisation fichier désactivée ({ex.GetType().Name}: {ex.Message}). " +
                $"Répertoire visé : '{_directory}'.");
          }
        }
      }
    }

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
      private readonly FileLoggerProvider _provider;
      private readonly string _category;

      public FileLogger(FileLoggerProvider provider, string category)
      {
        _provider = provider;
        _category = category;
      }

      public IDisposable BeginScope<TState>(TState state)
      {
        return NullScope.Instance;
      }

      public bool IsEnabled(LogLevel logLevel)
      {
        return _provider.IsEnabled(logLevel);
      }

      public void Log<TState>(
          LogLevel logLevel,
          EventId eventId,
          TState state,
          Exception exception,
          Func<TState, Exception, string> formatter)
      {
        if (!IsEnabled(logLevel) || formatter == null)
        {
          return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
        {
          return;
        }

        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z [{logLevel}] {_category}: {message}";
        if (exception != null)
        {
          line += Environment.NewLine + exception;
        }

        _provider.Write(line);
      }
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new NullScope();

      private NullScope()
      {
      }

      public void Dispose()
      {
      }
    }
  }
}
