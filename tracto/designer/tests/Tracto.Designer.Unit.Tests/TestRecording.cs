namespace Tracto.Designer.Unit.Tests;

using Moq;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Adapters;
using Tracto.Designer.Core.Audit;
using Tracto.Designer.Core.Browser;
using Tracto.Designer.Core.Credentials;
using Tracto.Designer.Core.Desktop;
using Tracto.Designer.Core.IsolatedMode;
using Tracto.Designer.Core.Mcp;
using Tracto.Designer.Core.OfflineExport;
using Tracto.Designer.Core.Privacy;
using Tracto.Designer.Core.Publication;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;
using Tracto.Designer.Core.Templates;
using Tracto.Designer.Core.Testing;
using Tracto.Designer.Core.Validation;

/// <summary>
/// Builds the window's view-model with every dependency stubbed, for tests that need the whole
/// thing rather than one piece of it.
/// </summary>
public static class TestRecording
{
    public static RecordingViewModel BuildViewModel(out IRecordingSessionService session)
    {
        var real = new RecordingSessionService();
        session = real;

        var voice = new Mock<IVoiceTranscriptionAdapter>();
        voice.SetupGet(v => v.IsAvailable).Returns(false);

        var templates = new Mock<ITemplateLibrary>();
        templates.Setup(t => t.GetAll()).Returns([]);
        templates.Setup(t => t.GetByCategory(It.IsAny<TemplateCategory>())).Returns([]);
        templates.Setup(t => t.Search(It.IsAny<string>())).Returns([]);

        var vault = new Mock<ICredentialVault>();
        vault.Setup(v => v.ListCredentials()).Returns([]);

        var auditLog = new Mock<IAuditLog>();
        auditLog.Setup(a => a.GetEntries(It.IsAny<AuditCategory?>())).Returns([]);

        var runner = new Mock<IScriptTestRunner>();
        runner.SetupGet(r => r.Name).Returns("test runner");

        var signer = new Mock<IScriptSigner>();
        signer.SetupGet(s => s.SignerId).Returns("author");

        var publisher = new Mock<IScriptPublisher>();
        publisher.SetupGet(p => p.Name).Returns("test publisher");

        return new RecordingViewModel(
            real,
            new Mock<IDesktopCaptureEngine>().Object,
            voice.Object,
            new ScriptDraftGenerator(),
            new Mock<IScriptValidator>().Object,
            new Mock<IMcpAuthoringServer>().Object,
            runner.Object,
            signer.Object,
            publisher.Object,
            new AuditService(auditLog.Object),
            new PrivacySettings(),
            templates.Object,
            new BrowserCaptureAdapter(new Mock<IBrowserCaptureEngine>().Object, real),
            vault.Object,
            new IsolatedModeSettings(),
            new IsolatedModeValidator(),
            new Mock<IOfflineExporter>().Object,
            new Mock<IBrowserProfileSignIn>().Object);
    }
}
