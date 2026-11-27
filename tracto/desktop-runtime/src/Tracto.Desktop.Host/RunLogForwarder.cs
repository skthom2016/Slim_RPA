#nullable enable

using Microsoft.Extensions.Logging;
using Tracto.Desktop.Protocol;

namespace Tracto.Desktop.Host;

/// <summary>
/// Copies what the host logs into the <see cref="RunLogSink"/>, so the same lines a person reads
/// in the worker's console are kept against the run that caused them: every warning and error, and
/// the Info lines of the step engine (see <see cref="RunLogSink.IsKept"/>). Attaching to the
/// logging pipeline rather than calling the sink by hand means a warning added anywhere in the
/// runtime is captured without anyone remembering to.
/// </summary>
internal sealed class RunLogForwarder : ILoggerProvider
{
    private readonly RunLogSink _sink;

    public RunLogForwarder(RunLogSink sink) => _sink = sink;

    public ILogger CreateLogger(string categoryName) => new ForwardingLogger(_sink, categoryName);

    public void Dispose() { }

    private sealed class ForwardingLogger : ILogger
    {
        private readonly RunLogSink _sink;

        // The last segment of the category is enough to say where a line came from; the full
        // namespace is noise in a console the width of a table cell.
        private readonly string _shortCategory;

        public ForwardingLogger(RunLogSink sink, string category)
        {
            _sink = sink;
            _shortCategory = category[(category.LastIndexOf('.') + 1)..];
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => RunLogSink.IsKept(logLevel, _shortCategory);

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (exception is not null)
                message = $"{message} — {exception.GetType().Name}: {exception.Message}";
            // A window, a page or an error can quote a secret: it is kept out of the run log.
            message = SecretMask.Apply(message);

            // The step engine's own lines already say which step they are about ("[step.3] OK"):
            // that goes in the step column, and the logger name would only repeat itself.
            var step = System.Text.RegularExpressions.Regex.Match(message, @"^\[(step\.[0-9]+)\]");
            var stepId = step.Success ? step.Groups[1].Value : null;
            var text = _shortCategory == RunLogSink.StepEngineCategory ? message : $"[{_shortCategory}] {message}";

            _sink.Add(logLevel, text, stepId);
        }
    }
}
