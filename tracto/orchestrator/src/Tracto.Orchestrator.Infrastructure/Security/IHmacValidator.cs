#nullable enable
namespace Tracto.Orchestrator.Infrastructure.Security;

/// <summary>
/// Infrastructure-layer alias for <see cref="Tracto.Orchestrator.Application.Abstractions.IHmacValidator"/>.
/// Having this alias in the Infrastructure namespace lets infrastructure code resolve
/// IHmacValidator without importing Application.Abstractions directly.
/// The implementation <see cref="HmacValidator"/> satisfies both interfaces.
/// </summary>
public interface IHmacValidator : Tracto.Orchestrator.Application.Abstractions.IHmacValidator
{
    // No additional members — namespace-scoped alias only.
}
