#nullable enable
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tracto.Orchestrator.Application.Jobs;

/// <summary>
/// OC-02: Stub hosted service that will fire scheduled jobs when the cron engine is implemented.
/// Currently logs a startup message so the container wires and starts without errors.
/// </summary>
public sealed class CronTriggerService : IHostedService
{
    private readonly ILogger<CronTriggerService> _logger;

    public CronTriggerService(ILogger<CronTriggerService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("CronTriggerService running.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("CronTriggerService stopped.");
        return Task.CompletedTask;
    }
}
