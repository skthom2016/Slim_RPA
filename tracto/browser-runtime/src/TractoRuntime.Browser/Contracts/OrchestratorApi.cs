namespace TractoRuntime.Browser.Contracts;

public sealed record WorkerRegistration
{
    public required string WorkerId { get; init; }
    public required string RuntimeType { get; init; }
    public required IReadOnlyList<string> Capabilities { get; init; }
    public string? CapabilityDigest { get; init; }
}

public sealed record LeaseAcquireRequest
{
    public required string WorkerId { get; init; }
    public required string RunId { get; init; }
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record LeaseAcquireResult
{
    public required string LeaseToken { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed record HeartbeatRequest
{
    public required string LeaseToken { get; init; }
    public required string RunId { get; init; }
}

public sealed record CallbackNotification
{
    public required string RunId { get; init; }
    public required string CallbackToken { get; init; }
    public required string AuthTag { get; init; }
    public required ServiceOutcome ServiceOutcome { get; init; }
    public Dictionary<string, object?> Payload { get; init; } = new();
}
