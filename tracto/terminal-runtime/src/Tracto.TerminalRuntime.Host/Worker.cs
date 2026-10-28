using Tracto.TerminalRuntime.Application.Shells;

namespace Tracto.TerminalRuntime.Host;

/// <summary>
/// Background worker that initialises shell capabilities on start-up
/// and then enters the main processing loop.
/// </summary>
public sealed class Worker(
    ShellValidationService shellValidation,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // AC-1: detect available shells and record their versions on worker start.
        var advertisement = await shellValidation.InitialiseAsync(stoppingToken);

        logger.LogInformation(
            "Shell capability advertisement ready. Available shells: {Shells}",
            string.Join(", ", advertisement.AvailableShells
                .Where(s => s.IsAvailable)
                .Select(s => $"{s.ShellType}@{s.Version}")));

        // TODO (TER-US-002+): poll Orchestrator for jobs and dispatch them.
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
