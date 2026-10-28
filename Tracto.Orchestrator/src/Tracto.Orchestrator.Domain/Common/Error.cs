#nullable enable
namespace Tracto.Orchestrator.Domain.Common;

/// <summary>Represents a domain error with a machine code and human-readable message.</summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>True when this is the sentinel <see cref="None"/> value.</summary>
    public bool IsNone => Code.Length == 0 && Message.Length == 0;

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>Creates an error with an explicit code and message.</summary>
    public static Error Create(string code, string message) => new(code, message);

    /// <summary>Creates a validation error (code: Validation).</summary>
    public static Error Validation(string message) => new("Validation", message);

    /// <summary>Creates a not-found error (code: NotFound).</summary>
    public static Error NotFound(string message) => new("NotFound", message);

    /// <summary>Creates a conflict error (code: Conflict) — use for AttemptId mismatches, duplicates, etc.</summary>
    public static Error Conflict(string message) => new("Conflict", message);

    /// <summary>Creates a conflict error with an explicit machine code and human-readable message.</summary>
    public static Error Conflict(string code, string message) => new(code, message);

    /// <summary>Creates an error with a caller-supplied code; alias for <see cref="Create"/>.</summary>
    public static Error General(string code, string message) => new(code, message);

    public override string ToString() => $"[{Code}] {Message}";
}
