namespace TractoRuntime.Browser.Contracts;

/// <summary>CEC v1.0.1 wire envelope for run requests.</summary>
public sealed record CecRunRequest
{
    public required string RunId { get; init; }
    public required string WorkerId { get; init; }
    public required string ScriptRef { get; init; }
    public string? DeduplicationKey { get; init; }
    public ScriptParams? Params { get; init; }
}

public sealed record ScriptParams
{
    public Dictionary<string, object?> Values { get; init; } = new();
}

/// <summary>CEC v1.0.1 run outcome report.</summary>
public sealed record CecRunResult
{
    public required string RunId { get; init; }
    public required RunState State { get; init; }
    public ServiceOutcome? ServiceOutcome { get; init; }
    public CecError? Error { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public Dictionary<string, object?> Evidence { get; init; } = new();

    /// <summary>
    /// When <see langword="true"/>, an authorized human operator must review the run before
    /// any continuation is created.  This is a flag, not a run state (CEC-016).
    /// Set by the runtime when an MFA/CAPTCHA challenge (WEB-009 / BRW-US-012 AC#5) or
    /// an expired session (WEB-009 / BRW-US-012 AC#6) is detected.
    /// </summary>
    public bool NeedsAttention { get; init; }

    /// <summary>
    /// Set to <c>"REJECTED"</c> when <see cref="ServiceOutcome"/> is
    /// <see cref="Contracts.ServiceOutcome.BUSINESS_REJECTION"/> and the run terminates as
    /// <see cref="RunState.SUCCEEDED"/> (CEC-005 / BRW-US-018 AC#2).
    /// <para>
    /// A value of <c>"REJECTED"</c> here means the registered service definitively rejected
    /// the business request; the technical run itself succeeded (all required UI steps
    /// completed correctly).  This is <c>null</c> for all other service outcomes.
    /// </para>
    /// </summary>
    public string? BusinessOutcome { get; init; }
}

public sealed record CecError
{
    public required ErrorCategory Category { get; init; }
    public required string Message { get; init; }
    public string? DetailCode { get; init; }
}

public enum RunState
{
    SUCCEEDED,
    FAILED,
    CANCELLED,
    TIMED_OUT,
    UNKNOWN,
    HANDED_OFF
}

public enum ServiceOutcome
{
    SUCCESS,
    BUSINESS_REJECTION,
    TECHNICAL_FAILURE,
    TRANSIENT_FAILURE,
    UNKNOWN_OUTCOME
}

/// <summary>Exact 11 CEC §6.2 error categories — no additions permitted.</summary>
public enum ErrorCategory
{
    VALIDATION,
    POLICY_DENIED,
    TARGET_NOT_FOUND,
    TARGET_AMBIGUOUS,
    TIMEOUT,
    SESSION_UNAVAILABLE,
    ENVIRONMENT,
    TRANSPORT,
    AUTHENTICATION,
    CANCELLED,
    INTERNAL
}
