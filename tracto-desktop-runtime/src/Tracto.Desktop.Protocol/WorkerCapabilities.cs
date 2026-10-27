#nullable enable

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Describes the static capability advertisement for the Desktop Runtime worker.
/// RuntimeType is always "desktop"; Namespaces covers the desktop.* action namespace.
/// </summary>
public sealed record WorkerCapabilities
{
    /// <summary>Fixed runtime type identifier for the Desktop Runtime.</summary>
    public string RuntimeType { get; init; } = "desktop";

    /// <summary>Glob-style namespace patterns covering all action types the Desktop worker can execute.</summary>
    public string[] ActionNamespaces { get; init; } = ["desktop.*"];

    /// <summary>Glob-style namespace patterns covering all predicate namespaces the Desktop worker evaluates.</summary>
    public string[] PredicateNamespaces { get; init; } = ["desktop.*"];

    /// <summary>Maximum iteration count the worker enforces for control.repeatBounded steps.</summary>
    public int MaxIterations { get; init; } = 1000;

    /// <summary>Schema major versions supported by this worker.</summary>
    public int[] SupportedSchemaMajors { get; init; } = [1];
}
