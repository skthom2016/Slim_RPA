namespace TractoRuntime.Browser.Packaging;

/// <summary>
/// Validates that an isolated offline Browser Runtime bundle is complete and structurally
/// correct before any run is allowed to start (BRW-US-021 AC#1, AC#2, AC#3).
///
/// <para>
/// The validator checks that:
/// <list type="bullet">
///   <item>A pinned Playwright for .NET version reference is present in the bundle (AC#3).</item>
///   <item>Every browser channel declared in the manifest has a binary file on disk (AC#1, AC#3).</item>
/// </list>
/// </para>
///
/// <para>
/// The validator NEVER downloads, installs, or repairs missing browser binaries (AC#2).
/// When a binary is absent, it throws <see cref="OfflineBundleException"/> and returns control
/// to the caller immediately.
/// </para>
/// </summary>
public interface IOfflineBundleValidator
{
    /// <summary>
    /// Validates the bundle described by <paramref name="descriptor"/>.
    /// </summary>
    /// <param name="descriptor">The offline bundle to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="OfflineBundleException">
    /// Thrown when the Playwright version reference is absent, or when one or more browser
    /// binary files declared in the manifest are not present on disk.
    /// </exception>
    Task ValidateAsync(OfflineBundleDescriptor descriptor, CancellationToken ct);
}
