using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Application;

/// <summary>
/// Builds and returns the current worker capability advertisement.
/// Infrastructure implementation populates all fields from configuration.
/// </summary>
public interface ICapabilityAdvertiser
{
    Result<WorkerCapabilityAdvertisement, Error> BuildAdvertisement();
}
