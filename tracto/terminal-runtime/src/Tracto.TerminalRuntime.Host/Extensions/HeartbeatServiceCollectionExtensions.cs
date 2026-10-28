using Microsoft.Extensions.DependencyInjection;
using Tracto.TerminalRuntime.Application.Heartbeat;
using Tracto.TerminalRuntime.Infrastructure.Heartbeat;

namespace Tracto.TerminalRuntime.Host.Extensions;

/// <summary>
/// DI wiring for the heartbeat subsystem (TER-US-018).
/// Call <see cref="AddHeartbeatServices"/> from the host builder to register all
/// heartbeat-related services.
/// </summary>
public static class HeartbeatServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="OrchestratorHeartbeatSender"/>, the
    /// <see cref="HeartbeatService"/> (as both singleton and hosted service),
    /// and the <see cref="HeartbeatSettings"/> configuration.
    ///
    /// <para>
    /// DI layout (Host layer only — no business logic here):
    /// <list type="bullet">
    ///   <item><see cref="IHeartbeatSender"/> → <see cref="OrchestratorHeartbeatSender"/> (via typed <see cref="System.Net.Http.HttpClient"/>)</item>
    ///   <item><see cref="HeartbeatService"/> singleton registered as <see cref="Microsoft.Extensions.Hosting.IHostedService"/></item>
    ///   <item><see cref="HeartbeatSettings"/> bound from <c>"TerminalRuntime:Heartbeat"</c> unless overridden</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    ///   Optional inline configuration delegate.  When null, settings are bound from
    ///   <c>appsettings.json</c> under the <c>"TerminalRuntime:Heartbeat"</c> section.
    /// </param>
    public static IServiceCollection AddHeartbeatServices(
        this IServiceCollection services,
        Action<HeartbeatSettings>? configure = null)
    {
        // Typed HttpClient for the sender — factory manages lifetime.
        services.AddHttpClient<IHeartbeatSender, OrchestratorHeartbeatSender>();

        // Configuration binding.
        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<HeartbeatSettings>()
                    .BindConfiguration("TerminalRuntime:Heartbeat");

        // HeartbeatService is both a singleton (so StartSession/StopSession are callable
        // from command handlers via DI) and a hosted service (so the host manages its lifetime).
        services.AddSingleton<HeartbeatService>();
        services.AddHostedService(sp => sp.GetRequiredService<HeartbeatService>());

        return services;
    }
}
