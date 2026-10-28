using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.DependencyInjection;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Host;
using Tracto.TerminalRuntime.Host.Extensions;
using Tracto.TerminalRuntime.Infrastructure;
using Tracto.TerminalRuntime.Infrastructure.Clock;
using Tracto.TerminalRuntime.Infrastructure.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// Clock
builder.Services.AddSingleton<IClock, SystemClock>();

// TER-US-003: Working Directory Policy and Environment Variable Handling
var approvedRoots = builder.Configuration
    .GetSection("TerminalRuntime:ApprovedRoots")
    .Get<string[]>() ?? [];
builder.Services.AddTerminalSecurityServices(approvedRoots);

// Capability advertisement
builder.Services.Configure<CapabilityAdvertiserOptions>(
    builder.Configuration.GetSection("CapabilityAdvertiser"));
builder.Services.AddSingleton<ICapabilityAdvertiser, CapabilityAdvertiser>();

// Remote context policy
builder.Services.Configure<RemoteContextValidatorOptions>(
    builder.Configuration.GetSection("RemoteContextPolicy"));
builder.Services.AddSingleton<IRemoteContextValidator, RemoteContextValidator>();

// PID isolation tracker
builder.Services.AddSingleton<IProcessTracker, ProcessTracker>();

// TER-US-022: Run journal — singleton so lease state is consistent within the process.
builder.Services.AddSingleton<IRunJournal, JsonRunJournal>();

// TER-US-022: Lease acquisition and reconciliation handlers
builder.Services.AddTransient<AcquireLeaseCommandHandler>();
builder.Services.AddTransient<ReconcileRunHandler>();

// Shell detection and validation (TER-US-001)
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices();

// TER-US-013: Output Redaction and Secret Protection
// IOutputRedactor and ITerminalProcessRunner are already registered inside
// AddInfrastructureServices() (co-location).  This explicit call is retained so
// the registration intent is visible at the composition root and survives future
// refactors that might restructure the infrastructure extensions.
builder.Services.AddOutputRedactionServices();

// Background worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
