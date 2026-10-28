using Moq;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Binary;
using TractoRuntime.Browser.Conformance;
using TractoRuntime.Browser.Context;
using TractoRuntime.Browser.Environment;
using TractoRuntime.Browser.Failure;
using TractoRuntime.Browser.Lease;
using TractoRuntime.Browser.Navigation;
using TractoRuntime.Browser.Network;
using TractoRuntime.Browser.Packaging;
using TractoRuntime.Browser.Security;
using TractoRuntime.Browser.Service;
using TractoRuntime.Browser.Session;
using TractoRuntime.Browser.Viewport;
using TractoRuntime.Browser.Worker;

namespace TractoRuntime.Browser.Tests.Fixtures;

/// <summary>Base class providing mocks for all core interfaces.</summary>
public abstract class BrowserTestBase
{
    protected Mock<ICecWorker> WorkerMock { get; } = new();
    protected Mock<IRunJournal> JournalMock { get; } = new();
    protected Mock<IBrowserContextManager> ContextManagerMock { get; } = new();
    protected Mock<ILeaseManager> LeaseManagerMock { get; } = new();
    protected Mock<ISecretResolver> SecretResolverMock { get; } = new();
    protected Mock<IServiceInvoker> ServiceInvokerMock { get; } = new();
    protected Mock<IActionExecutor> ActionExecutorMock { get; } = new();
    protected Mock<IFailureClassifier> FailureClassifierMock { get; } = new();
    protected Mock<IBinaryValidator> BinaryValidatorMock { get; } = new();
    protected Mock<ISessionStore> SessionStoreMock { get; } = new();
    protected Mock<IConformanceReporter> ConformanceReporterMock { get; } = new();
    protected Mock<IRuntimePackage> RuntimePackageMock { get; } = new();
    protected Mock<IEnvironmentReporter> EnvironmentReporterMock { get; } = new();
    protected Mock<IViewportValidator> ViewportValidatorMock { get; } = new();
    protected Mock<IOriginPolicyEnforcer> OriginPolicyEnforcerMock { get; } = new();
    protected Mock<INetworkPolicyEnforcer> NetworkPolicyEnforcerMock { get; } = new();
    protected Mock<INavigationExecutor> NavigationExecutorMock { get; } = new();
    protected MockOrchestrator Orchestrator { get; } = new();
}
