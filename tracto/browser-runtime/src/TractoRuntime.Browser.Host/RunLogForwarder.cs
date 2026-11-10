using Microsoft.Extensions.Logging;
using TractoRuntime.Browser.Diagnostics;

namespace TractoRuntime.Browser.Host;

/// <summary>
/// Copies everything the host logs at Warning or above into the <see cref="RunLogSink"/>, so
/// the same lines a person reads in the worker's console are kept against the run that caused
/// them. Attaching to the logging pipeline rather than calling the sink by hand means a warning
/// added anywhere in the runtime is captured without anyone remembering to.
/// </summary>
public sealed class RunLogForwarder(RunLogSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ForwardingLogger(sink, categoryName);

    public void Dispose() { }

    private sealed class ForwardingLogger(RunLogSink sink, string category) : ILogger
    {
        // The last segment of the category is enough to say where a line came from; the full
        // namespace is noise in a console the width of a table cell.
        private readonly string _shortCategory = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => RunLogSink.LevelName(logLevel) is not null;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (exception is not null)
                message = $"{message} — {exception.GetType().Name}: {exception.Message}";

            sink.Add(logLevel, $"[{_shortCategory}] {message}");
        }
    }
}
