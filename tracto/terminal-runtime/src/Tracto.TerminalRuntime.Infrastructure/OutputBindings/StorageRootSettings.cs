using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Infrastructure.OutputBindings;

/// <summary>
/// Options bound from <c>appsettings.json</c> section <c>"StorageRoot"</c>.
/// Provides the list of approved root directories for output file paths and result artifacts.
/// </summary>
public sealed class StorageRootSettings
{
    public const string SectionName = "StorageRoot";

    /// <summary>
    /// Approved storage root directories. Paths outside these roots are rejected
    /// with <c>errorCategory=POLICY_DENIED</c> before any I/O is performed.
    /// </summary>
    public string[] ApprovedRoots { get; set; } = [];

    /// <summary>Converts these settings into a domain <see cref="StorageRootPolicy"/>.</summary>
    public StorageRootPolicy ToPolicy() => new(ApprovedRoots);
}
