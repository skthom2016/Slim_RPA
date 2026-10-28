using Microsoft.Extensions.DependencyInjection;
using Tracto.TerminalRuntime.Application.Outcomes;
using Tracto.TerminalRuntime.Infrastructure.Outcomes;

namespace Tracto.TerminalRuntime.Host.Extensions;

/// <summary>
/// DI wiring for run-outcome classification services (TER-US-016).
/// </summary>
public static class OutcomeServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRunOutcomeClassifier"/> → <see cref="RunOutcomeClassifier"/>.
    /// The classifier is stateless; a singleton is sufficient and avoids per-request allocation.
    /// </summary>
    public static IServiceCollection AddTerminalOutcomeServices(this IServiceCollection services)
    {
        services.AddSingleton<IRunOutcomeClassifier, RunOutcomeClassifier>();
        return services;
    }
}
