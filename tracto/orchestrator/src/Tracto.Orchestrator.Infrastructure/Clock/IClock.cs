using AppClock = Tracto.Orchestrator.Application.Abstractions.IClock;

namespace Tracto.Orchestrator.Infrastructure.Clock;

/// <summary>
/// Infrastructure-layer alias for <see cref="AppClock"/>.
/// Extends the Application abstraction so that <see cref="SystemClock"/> and
/// <see cref="FakeClock"/> satisfy both the Application and Infrastructure contracts.
/// </summary>
public interface IClock : AppClock { }
