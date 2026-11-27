#nullable enable
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Tracto.Desktop.Protocol;

namespace Tracto.Desktop.Host;

/// <summary>
/// One console line per log entry, with the run's credential values masked, so a value a page,
/// an application or an error quotes never reaches the console.
/// </summary>
internal sealed class MaskingConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "tracto-masked";

    public override void Write<TState>(
        in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (logEntry.Exception is not null) message += " " + logEntry.Exception;
        message = SecretMask.Apply(message).ReplaceLineEndings(" ");

        var level = logEntry.LogLevel switch
        {
            LogLevel.Trace => "trce", LogLevel.Debug => "dbug", LogLevel.Information => "info",
            LogLevel.Warning => "warn", LogLevel.Error => "fail", LogLevel.Critical => "crit", _ => "none",
        };
        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("HH:mm:ss ")).Append(level).Append(": ")
          .Append(logEntry.Category).Append('[').Append(logEntry.EventId.Id).Append("] ").Append(message);
        textWriter.WriteLine(sb.ToString());
    }
}