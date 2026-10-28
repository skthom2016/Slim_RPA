using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Policies;

namespace Tracto.TerminalRuntime.Application.Interfaces;

/// <summary>
/// Validates a working-directory reference (cwdRef) against a <see cref="WorkingDirPolicy"/>
/// before any I/O is performed.
/// </summary>
public interface IPathValidator
{
    /// <summary>
    /// Validates <paramref name="cwdRef"/> and returns its canonicalized form on success.
    /// </summary>
    /// <param name="cwdRef">The raw working-directory path supplied by the step definition.</param>
    /// <param name="policy">The policy that lists approved root directories.</param>
    /// <returns>
    ///   <see cref="Result{T,TError}.Ok"/> containing the canonicalized path, or
    ///   <see cref="Result{T,TError}.Fail"/> with errorCategory=POLICY_DENIED and
    ///   detailCode=terminal.pathTraversalDenied when the path is rejected.
    /// </returns>
    Result<string, Error> Validate(string cwdRef, WorkingDirPolicy policy);
}
