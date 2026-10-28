namespace Tracto.TerminalRuntime.Application.Execution;

/// <summary>
/// The redacted output produced by <see cref="ITerminalProcessRunner.RunAsync"/>.
/// All text fields contain only sanitised content — raw process output is never surfaced.
/// </summary>
/// <param name="StdoutContent">Redacted stdout text. Safe for journals, evidence, and Orchestrator payloads.</param>
/// <param name="StderrContent">Redacted stderr text. Safe for journals, evidence, and Orchestrator payloads.</param>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="WasRedacted">True when at least one token or heuristic match was replaced.</param>
public sealed record ProcessRunResult(
    string StdoutContent,
    string StderrContent,
    int    ExitCode,
    bool   WasRedacted);
