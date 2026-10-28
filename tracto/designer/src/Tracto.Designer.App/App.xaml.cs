namespace Tracto.Designer.App;

using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;
using Tracto.Designer.Core.Adapters;
using Tracto.Designer.Core.Desktop;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Infrastructure.Adapters;
using Tracto.Designer.Infrastructure.Desktop;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Mcp;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;
using Tracto.Designer.Core.Testing;
using Tracto.Designer.Core.Validation;
using Tracto.Designer.Infrastructure.Mcp;
using Tracto.Designer.Core.Publication;
using Tracto.Designer.Infrastructure.Publication;
using Tracto.Designer.Infrastructure.Scripting;
using Tracto.Designer.Core.Audit;
using Tracto.Designer.Core.Privacy;
using Tracto.Designer.Infrastructure.Audit;
using Tracto.Designer.Core.Browser;
using Tracto.Designer.Core.Templates;
using Tracto.Designer.Infrastructure.Browser;
using Tracto.Designer.Core.Credentials;
using Tracto.Designer.Core.IsolatedMode;
using Tracto.Designer.Core.OfflineExport;
using Tracto.Designer.Infrastructure.Credentials;
using Tracto.Designer.Infrastructure.OfflineExport;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var sc = new ServiceCollection();
        sc.AddSingleton<IRecordingSessionService, RecordingSessionService>();
        sc.AddSingleton<IAiAuthoringAdapter, NullAiAdapter>();
        sc.AddSingleton<IVoiceTranscriptionAdapter, WindowsVoiceAdapter>();
        var screenshotRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tracto", "Screenshots");
        sc.AddSingleton<IScreenshotAdapter>(_ => new WindowsScreenshotAdapter(screenshotRoot));
        sc.AddSingleton<IDesktopCaptureEngine, DesktopCaptureEngine>();
        sc.AddSingleton<IJabCaptureAdapter, NullJabAdapter>();
        sc.AddSingleton<IScriptDraftGenerator, ScriptDraftGenerator>();
        sc.AddSingleton<IScriptValidator, ScriptValidator>();
        sc.AddSingleton<IScriptSigner>(sp => new HmacScriptSigner(
            "tracto-designer-dev",
            System.Text.Encoding.UTF8.GetBytes("tracto-dev-signing-key-changeme"),
            sp.GetRequiredService<IScriptValidator>()));
        sc.AddSingleton<IScriptTestRunner, LocalSimulationTestRunner>();
        sc.AddSingleton<PublicationGate>();
        var orchestratorUrl = System.Environment.GetEnvironmentVariable("TRACTO_ORCHESTRATOR_URL")
            ?? "http://localhost:5080";
        sc.AddSingleton<IScriptPublisher>(sp => new OrchestratorPublisher(
            orchestratorUrl,
            sp.GetRequiredService<PublicationGate>()));
        sc.AddSingleton<IScriptRepository, FileScriptRepository>();
        sc.AddSingleton<McpToolHandler>();
        sc.AddSingleton<IMcpAuthoringServer, NamedPipeMcpServer>();
        // Audit
        var auditLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tracto", "Designer", "audit.jsonl");
        sc.AddSingleton<IAuditLog>(sp => new CompositeAuditLog(
            new InMemoryAuditLog(),
            new FileAuditLog(auditLogPath)));
        sc.AddSingleton<AuditService>();
        // Privacy
        sc.AddSingleton<PrivacySettings>();
        sc.AddSingleton<IPrivacyFilter, PrivacyFilter>();
        sc.AddSingleton<IBrowserCaptureEngine, NullBrowserCaptureEngine>(); // default to null; CdpBrowserCaptureEngine used when author connects
        sc.AddSingleton<BrowserCaptureAdapter>();
        sc.AddSingleton<ITemplateLibrary, BuiltInTemplateLibrary>();
        // DS-17 Credential Vault
        sc.AddSingleton<ICredentialVault, WindowsCredentialVault>();
        sc.AddSingleton<CredentialParameterResolver>();
        // DS-18 Isolated Mode
        sc.AddSingleton<IsolatedModeSettings>();
        sc.AddSingleton<IsolatedModeValidator>();
        // DS-19 Offline Export
        sc.AddSingleton<IOfflineExporter, ZipOfflineExporter>();
        sc.AddTransient<RecordingViewModel>();
        sc.AddTransient<ScriptEditorViewModel>();
        Services = sc.BuildServiceProvider();
    }
}
