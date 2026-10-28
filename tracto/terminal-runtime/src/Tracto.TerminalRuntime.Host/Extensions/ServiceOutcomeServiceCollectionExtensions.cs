using Microsoft.Extensions.DependencyInjection;
using Tracto.TerminalRuntime.Application.Service;
using Tracto.TerminalRuntime.Infrastructure.Service;

namespace Tracto.TerminalRuntime.Host.Extensions;

/// <summary>
/// DI wiring for service-outcome handling (TER-US-021).
///
/// Registers:
/// <list type="bullet">
///   <item><see cref="IServiceOutcomeHandler"/> → <see cref="HandleServiceOutcomeHandler"/></item>
///   <item><see cref="IServiceInvokeClient"/> → <see cref="ServiceInvokeClient"/>
///         (backed by a named <see cref="HttpClient"/>)</item>
/// </list>
/// </summary>
public static class ServiceOutcomeServiceCollectionExtensions
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> used by <see cref="ServiceInvokeClient"/>.
    /// Configure its base address and auth handlers in Program.cs or appsettings.json.
    /// </summary>
    public const string ServiceInvokeHttpClientName = "ServiceInvoke";

    /// <summary>
    /// Registers all service-outcome handling services into the DI container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddServiceOutcomeServices(this IServiceCollection services)
    {
        // IServiceOutcomeHandler is registered as Transient to match the most-transient
        // dependency it captures (IServiceHandoffHandler is Transient). Using Singleton
        // would create a captive dependency that defeats IServiceHandoffHandler's lifetime.
        services.AddTransient<IServiceOutcomeHandler, HandleServiceOutcomeHandler>();

        // IServiceInvokeClient depends on HttpClient; use IHttpClientFactory for
        // proper socket lifetime management.
        services.AddHttpClient<IServiceInvokeClient, ServiceInvokeClient>(
            ServiceInvokeHttpClientName);

        return services;
    }
}
