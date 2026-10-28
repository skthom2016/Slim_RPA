using Microsoft.Extensions.DependencyInjection;
using Tracto.TerminalRuntime.Application.Identity;
using Tracto.TerminalRuntime.Application.Outcomes;
using Tracto.TerminalRuntime.Application.Shells;

namespace Tracto.TerminalRuntime.Application.DependencyInjection;

/// <summary>
/// Extension methods to wire Application-layer services into the DI container.
/// </summary>
public static class ApplicationServiceExtensions
{
    /// <summary>Registers all Application-layer services.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<ShellValidationService>();

        // TER-US-020: Signed Artifact Validation handler (transient — stateless per command)
        services.AddTransient<ValidateArtifactCommandHandler>();

        // TER-US-002: Execution Identity and Privilege Enforcement
        services.AddTransient<VerifyIdentityHandler>();

        // TER-US-016: Unknown Side Effect Classification — interface registered here;
        // concrete implementation (RunOutcomeClassifier) wired in Host via
        // AddTerminalOutcomeServices() so the Infrastructure assembly stays out of Application.
        // The interface itself lives in Application for clean dependency inversion.

        return services;
    }
}
