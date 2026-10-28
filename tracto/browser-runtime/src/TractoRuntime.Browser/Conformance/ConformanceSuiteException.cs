namespace TractoRuntime.Browser.Conformance;

/// <summary>
/// Thrown by <see cref="ConformanceReporter"/> when one or more CF-01–CF-10 fixtures fail.
/// BRW-US-020 AC#1 — all fixtures must pass before the runtime is considered conformant.
/// </summary>
public sealed class ConformanceSuiteException(string message) : Exception(message);
