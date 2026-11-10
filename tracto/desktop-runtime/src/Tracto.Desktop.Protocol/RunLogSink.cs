#nullable enable

using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Protocol;

/// <summary>One line the runtime logged while it was running a job.</summary>
/// <param name="Level">"Warning" or "Error" — the only levels kept.</param>
/// <param name="StepId">The step being run when it was logged, when one was.</param>
/// <param name="Message">The line as it appeared in the worker's console.</param>
/// <param name="OccurredAt">When it was logged, in UTC.</param>
public sealed record RunLogEntry(string Level, string? StepId, string Message, DateTimeOffset OccurredAt);

/// <summary>
/// Collects what the worker logs while it runs one job, so the Orchestrator can keep it and a
/// failure can be explained afterwards from the console rather than guessed at.
/// </summary>
/// <remarks>
/// Only Warning and above is collected: Info is the worker's running commentary and would bury
/// the lines that explain a failure, which is what these are for. The buffer is capped, so a
/// step that fails in a loop cannot fill memory or the database — once full, further entries
/// are counted and dropped, and the count is reported in place of them.
/// <para>
/// A worker runs one job at a time, so a single current run is all this tracks. Entries logged
/// while no run is active are ignored: they belong to the worker, not to a job.
/// </para>
/// </remarks>
public sealed class RunLogSink
{
    private readonly object _gate = new();
    private readonly List<RunLogEntry> _entries = new();
    private readonly int _capacity;
    private int _dropped;

    /// <summary>Creates a sink holding at most <paramref name="capacity"/> entries per run.</summary>
    /// <param name="capacity">How many entries one run may keep before further lines are counted and dropped.</param>
    public RunLogSink(int capacity = 500) => _capacity = capacity;

    public string? CurrentRunId { get; private set; }
    public string? CurrentAttemptId { get; private set; }

    /// <summary>Starts collecting for a job, discarding anything left from the last one.</summary>
    /// <param name="runId">The run the following lines belong to.</param>
    /// <param name="attemptId">The attempt the Orchestrator leased, when known.</param>
    public void BeginRun(string runId, string? attemptId)
    {
        lock (_gate)
        {
            _entries.Clear();
            _dropped = 0;
            CurrentRunId = runId;
            CurrentAttemptId = attemptId;
        }
    }

    /// <summary>
    /// Records one line if a run is active and the level is worth keeping.
    /// </summary>
    /// <param name="level">The level it was logged at; Info and below are ignored.</param>
    /// <param name="message">The line to keep.</param>
    /// <param name="stepId">The step being run at the time, when one is known.</param>
    /// <returns>Whether the line was kept.</returns>
    public bool Add(LogLevel level, string message, string? stepId = null)
    {
        var name = LevelName(level);
        if (name is null || string.IsNullOrWhiteSpace(message)) return false;

        lock (_gate)
        {
            if (CurrentRunId is null) return false;
            if (_entries.Count >= _capacity)
            {
                _dropped++;
                return false;
            }
            _entries.Add(new RunLogEntry(name, stepId, message.Trim(), DateTimeOffset.UtcNow));
            return true;
        }
    }

    /// <summary>
    /// Takes everything collected and stops collecting, so the lines cannot be sent twice. A
    /// note is appended when entries were dropped, rather than letting the gap go unexplained.
    /// </summary>
    /// <returns>Everything collected for the run that just ended.</returns>
    public IReadOnlyList<RunLogEntry> EndRunAndDrain()
    {
        lock (_gate)
        {
            var taken = new List<RunLogEntry>(_entries);
            if (_dropped > 0)
                taken.Add(new RunLogEntry("Warning", null,
                    $"{_dropped} further log entries were dropped; this run's buffer holds {_capacity}.",
                    DateTimeOffset.UtcNow));

            _entries.Clear();
            _dropped = 0;
            CurrentRunId = null;
            CurrentAttemptId = null;
            return taken;
        }
    }

    /// <summary>The name the Orchestrator stores, or null for a level that is not kept.</summary>
    /// <param name="level">The level to name.</param>
    /// <returns>"Warning", "Error", or null.</returns>
    public static string? LevelName(LogLevel level) => level switch
    {
        LogLevel.Warning => "Warning",
        LogLevel.Error or LogLevel.Critical => "Error",
        _ => null,
    };
}
