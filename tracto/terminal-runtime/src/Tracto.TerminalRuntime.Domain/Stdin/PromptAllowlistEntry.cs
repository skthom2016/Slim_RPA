namespace Tracto.TerminalRuntime.Domain.Stdin;

/// <summary>
/// An entry in the stdin prompt allowlist.
/// The <see cref="Pattern"/> is a .NET regular expression matched (case-insensitive)
/// against each stdout line emitted by the running process.
/// When a match is found the <see cref="ApprovedResponse"/> is written to stdin.
///
/// No credential may appear as an <see cref="ApprovedResponse"/> value — the field is
/// intentionally kept opaque and is NEVER reflected into logs, evidence, or telemetry
/// (CEC v1.0.1 invariant #4).
/// </summary>
/// <param name="Pattern">
///   .NET regex tested against an individual stdout output line.
///   Must be a syntactically valid regex; callers are responsible for pre-validating
///   patterns before constructing this record.
/// </param>
/// <param name="ApprovedResponse">
///   The pre-authorised response string written verbatim to process stdin when
///   <see cref="Pattern"/> is matched.  Never logged.
/// </param>
public sealed record PromptAllowlistEntry(string Pattern, string ApprovedResponse);
