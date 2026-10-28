#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Jobs;

namespace Tracto.Orchestrator.Infrastructure.Recovery;

/// <summary>
/// Singleton BackgroundService that drives ReconciliationService on a fixed
/// ReconciliationService.ScanInterval (15 s) — OC-10.
///
/// Follows the same scope-per-tick pattern as LeaseExpiryMonitorWorker:
/// IHostedService instances live in the singleton root container; all Scoped
/// dependencies (repositories, UnitOfWork, DbContext) are resolved through a
/// per-tick IServiceScope so they share a single DbContext per scan cycle.
/// </summary>
public sealed class ReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory          _scopeFactory;
    private readonly ILogger<ReconciliationWorker> _logger;

    public ReconciliationWorker(
        IServiceScopeFactory          scopeFactory,
        ILogger<ReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ReconciliationWorker started (scanInterval={Interval}s).",
            ReconciliationService.ScanInterval.TotalSeconds);

        using var timer = new PeriodicTimer(ReconciliationService.ScanInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider
                                   .GetRequiredService<ReconciliationService>();

                await service.ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ReconciliationWorker: unhandled exception during scan tick — " +
                    "will retry on next interval.");
            }
        }

        _logger.LogInformation("ReconciliationWorker stopped.");
    }
}
