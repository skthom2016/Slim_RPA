using Microsoft.Extensions.DependencyInjection;
using Tracto.TerminalRuntime.Application.Interfaces;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Policies;
using Tracto.TerminalRuntime.Infrastructure.Security;

namespace Tracto.TerminalRuntime.Host.Extensions;

/// <summary>
/// DI wiring for working-directory policy and environment variable handling (TER-US-003).
/// Registers the <see cref="WorkingDirPolicy"/> singleton from configuration so that
/// <see cref="IPathValidator"/> implementations can be given the policy via DI.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IPathValidator"/>, <see cref="IEnvironmentBuilder"/>,
    /// <see cref="ISecretStore"/>, and the <see cref="WorkingDirPolicy"/> with the DI container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="approvedRoots">
    ///   The list of root directories permitted as working directories.
    ///   Typically sourced from appsettings.json under "TerminalRuntime:ApprovedRoots".
    /// </param>
    public static IServiceCollection AddTerminalSecurityServices(
        this IServiceCollection services,
        string[] approvedRoots)
    {
        services.AddSingleton(new WorkingDirPolicy(approvedRoots));
        services.AddSingleton<IPathValidator, PathValidator>();
        services.AddSingleton<ISecretStore, StubSecretStore>();
        services.AddSingleton<IEnvironmentBuilder, EnvironmentBuilder>();
        return services;
    }
}
