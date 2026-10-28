#nullable enable

using System;

namespace Tracto.Contract.Orchestrator;

/// <summary>Result returned by the orchestrator when a worker attempts to register.</summary>
public sealed record RegistrationResult
{
    /// <summary>Whether the registration was accepted.</summary>
    public bool IsAccepted { get; init; }

    /// <summary>The registered worker identifier; non-null when <see cref="IsAccepted"/> is <c>true</c>.</summary>
    public string WorkerId { get; init; } = string.Empty;

    /// <summary>Short error code when registration was refused; <c>null</c> when accepted.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable message when registration was refused; <c>null</c> when accepted.</summary>
    public string? Message { get; init; }

    private RegistrationResult() { }

    /// <summary>Creates an accepted registration result for the given worker.</summary>
    public static RegistrationResult Accepted(string workerId)
    {
        if (workerId is null) throw new ArgumentNullException(nameof(workerId));
        return new RegistrationResult { IsAccepted = true, WorkerId = workerId };
    }

    /// <summary>Creates a refused registration result with an error code and message.</summary>
    public static RegistrationResult Refused(string errorCode, string message)
    {
        if (errorCode is null) throw new ArgumentNullException(nameof(errorCode));
        if (message is null) throw new ArgumentNullException(nameof(message));
        return new RegistrationResult { IsAccepted = false, ErrorCode = errorCode, Message = message };
    }
}
