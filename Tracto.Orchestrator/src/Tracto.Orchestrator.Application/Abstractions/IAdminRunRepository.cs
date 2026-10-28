#nullable enable
namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Obsolete: superseded by <see cref="IAdminJobRepository"/> (OC-13).
/// This interface is retained as an empty marker to avoid breaking any
/// future references that may still import this file.
/// All projection records previously defined here are now in IAdminJobRepository.cs.
/// </summary>
[Obsolete("Use IAdminJobRepository instead.", error: false)]
public interface IAdminRunRepository { }
