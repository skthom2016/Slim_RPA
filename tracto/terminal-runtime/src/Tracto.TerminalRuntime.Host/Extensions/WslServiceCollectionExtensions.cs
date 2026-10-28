using Microsoft.Extensions.DependencyInjection;
using Tracto.TerminalRuntime.Application.Wsl;
using Tracto.TerminalRuntime.Infrastructure.Wsl;

namespace Tracto.TerminalRuntime.Host.Extensions;

/// <summary>
/// DI wiring for WSL health monitoring and outage handling (TER-US-017).
/// </summary>
public static class WslServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IWslHealthMonitor"/> and <see cref="HandleWslOutageHandler"/>
    /// with the DI container.
    /// </summary>
    public static IServiceCollection AddWslServices(this IServiceCollection services)
    {
        // IWslHealthMonitor: singleton — stateless polling wrapper around wsl.exe.
        services.AddSingleton<IWslHealthMonitor, WslHealthMonitor>();

        // HandleWslOutageHandler: transient — stateless command handler.
        services.AddTransient<HandleWslOutageHandler>();

        return services;
    }
}
