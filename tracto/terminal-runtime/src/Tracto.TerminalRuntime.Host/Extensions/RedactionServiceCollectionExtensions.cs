using Microsoft.Extensions.DependencyInjection;
using Tracto.TerminalRuntime.Application.Redaction;
using Tracto.TerminalRuntime.Infrastructure.Redaction;

namespace Tracto.TerminalRuntime.Host.Extensions;

/// <summary>
/// DI wiring for output redaction services (TER-US-013).
/// </summary>
public static class RedactionServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IOutputRedactor"/> with the DI container.
    /// </summary>
    public static IServiceCollection AddOutputRedactionServices(
        this IServiceCollection services)
    {
        services.AddSingleton<IOutputRedactor, OutputRedactor>();
        return services;
    }
}
