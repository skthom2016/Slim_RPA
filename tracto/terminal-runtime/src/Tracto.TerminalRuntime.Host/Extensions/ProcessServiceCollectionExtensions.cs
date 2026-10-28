using Microsoft.Extensions.DependencyInjection;
using Tracto.TerminalRuntime.Application.Process;
using Tracto.TerminalRuntime.Infrastructure.Process;

namespace Tracto.TerminalRuntime.Host.Extensions;

/// <summary>
/// DI registration for TER-US-015: process lifecycle services.
///
/// Wires:
/// <list type="bullet">
///   <item><see cref="IProcessController"/> → <see cref="WindowsProcessController"/> (singleton)</item>
///   <item><see cref="StartProcessHandler"/> (transient)</item>
///   <item><see cref="ProcessExecutionHandler"/> (transient)</item>
/// </list>
/// </summary>
public static class ProcessServiceCollectionExtensions
{
    /// <summary>
    /// Registers process-lifecycle services required by TER-US-015.
    /// Must be called after IRunJournal, IProcessTracker, and IClock are registered.
    /// </summary>
    public static IServiceCollection AddProcessServices(this IServiceCollection services)
    {
        services.AddSingleton<IProcessController, WindowsProcessController>();
        services.AddTransient<StartProcessHandler>();
        services.AddTransient<ProcessExecutionHandler>();

        return services;
    }
}
