namespace Tracto.TerminalRuntime.Application.Commands;

/// <summary>
/// Computes or verifies the SHA-256 digest of a script or binary file.
/// Implemented in Infrastructure; consumed by Application handlers.
/// No file I/O or process-launching logic belongs in this interface's callers.
/// </summary>
public interface ICommandHashVerifier
{
    /// <summary>
    /// Computes the SHA-256 hex digest of the file at <paramref name="filePath"/>
    /// and returns <c>true</c> when it matches <paramref name="expectedHex"/>
    /// (case-insensitive, hex-encoded).
    /// Returns <c>false</c> when the file does not exist, cannot be read, or the
    /// digest differs from <paramref name="expectedHex"/>.
    /// </summary>
    bool VerifyFileHash(string filePath, string expectedHex);
}
