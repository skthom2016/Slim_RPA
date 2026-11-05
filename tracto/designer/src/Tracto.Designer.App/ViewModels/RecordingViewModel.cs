namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Adapters;
using Tracto.Designer.Core.Browser;
using Tracto.Designer.Core.Desktop;
using Tracto.Designer.Core.Mcp;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;
using Tracto.Designer.Core.Testing;
using Tracto.Designer.Core.Publication;
using Tracto.Designer.Core.Audit;
using Tracto.Designer.Core.Privacy;
using Tracto.Designer.App.Services;
using Tracto.Designer.Core.Templates;
using Tracto.Designer.Core.Validation;
using Tracto.Designer.Core.IsolatedMode;
using Tracto.Designer.Core.Credentials;
using Tracto.Designer.Core.OfflineExport;

public sealed class RecordingViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IRecordingSessionService _session;
    private readonly IDesktopCaptureEngine _captureEngine;
    private readonly IVoiceTranscriptionAdapter _voice;
    private readonly IScriptDraftGenerator _draftGenerator;
    private CaptureEngineType? _selectedEngineType = CaptureEngineType.Desktop;
    private ScriptSegment? _watchedSegment;

    public RecordingViewModel(IRecordingSessionService session, IDesktopCaptureEngine captureEngine, IVoiceTranscriptionAdapter voice, IScriptDraftGenerator draftGenerator, IScriptValidator validator, IMcpAuthoringServer mcpServer, IScriptTestRunner testRunner, IScriptSigner scriptSigner, IScriptPublisher publisher, AuditService auditService, PrivacySettings privacySettings, ITemplateLibrary templateLibrary, BrowserCaptureAdapter browserCaptureAdapter, ICredentialVault credentialVault, IsolatedModeSettings isolatedModeSettings, IsolatedModeValidator isolatedModeValidator, IOfflineExporter offlineExporter, IBrowserProfileSignIn browserProfileSignIn)
    {
        _session = session;
        _captureEngine = captureEngine;
        _voice = voice;
        _draftGenerator = draftGenerator;
        _session.StateChanged += OnStateChanged;
        StartCommand         = new RelayCommand(_ => StartRecording(),     _ => IsIdle && SelectedEngineType.HasValue);
        PauseCommand         = new RelayCommand(_ => _session.Pause(),     _ => IsRecording);
        ResumeCommand        = new RelayCommand(_ => _session.Resume(),    _ => IsPaused);
        StopCommand          = new RelayCommand(_ => _session.Stop(),      _ => IsRecording || IsPaused);
        DiscardCommand       = new RelayCommand(_ => _session.Discard(),   _ => !IsStopped);
        GenerateDraftCommand = new RelayCommand(_ => ExecuteGenerateDraft(), _ => IsStopped && _session.Transcript.Count > 0);
        NewScriptCommand     = new RelayCommand(_ => StartNewScript());
        Validation = new ValidationViewModel(validator, ScriptEditor);
        AiBridge = new AiAuthoringViewModel(mcpServer);
        TestRunner = new TestPanelViewModel(testRunner, ScriptEditor);
        ScriptEditor.PropertyChanged += OnScriptEditorChanged;
        Signing      = new SigningViewModel(scriptSigner, ScriptEditor);
        Publication  = new PublicationViewModel(publisher, ScriptEditor, Signing, TestRunner);
        ExportImport = new ExportImportViewModel(ScriptEditor);
        Audit = new AuditViewModel(auditService);
        PrivacySettings = new PrivacySettingsViewModel(privacySettings);
        Templates = new TemplateLibraryViewModel(templateLibrary, ScriptEditor, new MessageBoxDialogService());
        BrowserAdapter = new BrowserAdapterViewModel(browserCaptureAdapter);
        ProfileSignIn = new ProfileSignInViewModel(browserProfileSignIn, BrowserAdapter);
        IsolatedMode = new IsolatedModeViewModel(isolatedModeSettings, isolatedModeValidator, ScriptEditor);
        Credentials = new CredentialVaultViewModel(credentialVault);
        OfflineExport = new OfflineExportViewModel(offlineExporter, ScriptEditor, Signing);
    }

    public IReadOnlyList<CaptureEngineType> EngineTypes { get; } =
        [CaptureEngineType.Desktop, CaptureEngineType.Browser, CaptureEngineType.Terminal];

    public CaptureEngineType? SelectedEngineType
    {
        get => _selectedEngineType;
        set
        {
            _selectedEngineType = value;
            OnPropertyChanged();
            ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
        }
    }

    // The segment being edited decides which engine records: there is no separate engine
    // picker. Selecting a browser segment records the browser, a desktop segment the desktop.
    private void OnScriptEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScriptEditorViewModel.IsSegmented) or nameof(ScriptEditorViewModel.SelectedSegment))
            WatchSelectedSegment();
    }

    // Also follows a runtime changed on the segment that is already selected.
    private void WatchSelectedSegment()
    {
        if (_watchedSegment is not null) _watchedSegment.PropertyChanged -= OnSegmentChanged;
        _watchedSegment = ScriptEditor.SelectedSegment;
        if (_watchedSegment is not null) _watchedSegment.PropertyChanged += OnSegmentChanged;
        SyncEngineToSegment();
    }

    private void OnSegmentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScriptSegment.Runtime)) SyncEngineToSegment();
    }

    private void SyncEngineToSegment()
    {
        if (_watchedSegment is null) return;
        SelectedEngineType = _watchedSegment.Runtime.ToLowerInvariant() switch
        {
            "browser"  => CaptureEngineType.Browser,
            "terminal" => CaptureEngineType.Terminal,
            _          => CaptureEngineType.Desktop
        };
    }

    public SessionState CurrentState => _session.CurrentState;
    public bool IsIdle      => _session.CurrentState == SessionState.Idle;
    public bool IsRecording => _session.CurrentState == SessionState.Recording;
    public bool IsPaused    => _session.CurrentState == SessionState.Paused;
    public bool IsStopped   => _session.CurrentState == SessionState.Stopped;
    public bool NarrationActive => _voice.IsAvailable;

    public ScriptEditorViewModel ScriptEditor { get; } = new();
    public ValidationViewModel Validation { get; }
    public AiAuthoringViewModel AiBridge { get; }
    public TestPanelViewModel TestRunner { get; }
    public SigningViewModel Signing { get; }
    public PublicationViewModel Publication { get; }
    public ExportImportViewModel ExportImport { get; }
    public AuditViewModel Audit { get; }
    public PrivacySettingsViewModel PrivacySettings { get; }
    public TemplateLibraryViewModel Templates { get; }
    public BrowserAdapterViewModel BrowserAdapter { get; }
    public ProfileSignInViewModel ProfileSignIn { get; }
    public IsolatedModeViewModel IsolatedMode { get; }
    public CredentialVaultViewModel Credentials { get; }
    public OfflineExportViewModel OfflineExport { get; }

    public ICommand StartCommand         { get; }
    public ICommand PauseCommand         { get; }
    public ICommand ResumeCommand        { get; }
    public ICommand StopCommand          { get; }
    public ICommand DiscardCommand       { get; }
    public ICommand GenerateDraftCommand { get; }
    public ICommand NewScriptCommand     { get; }

    /// <summary>
    /// Clears the editor and the recording session so another flow can be built without
    /// restarting the Designer. A stopped session cannot be discarded, so it is reset here.
    /// </summary>
    private void StartNewScript()
    {
        if (_captureEngine.IsRunning) _captureEngine.Stop();
        if (BrowserAdapter.IsConnected) _ = BrowserAdapter.StopAsync();
        _session.Reset();
        ScriptEditor.NewScript();
    }

    private void ExecuteGenerateDraft()
    {
        var sessionId = _session.SessionId;
        var shortId = sessionId is { Length: > 8 } ? sessionId[..8] : sessionId ?? "unknown";
        var script = _draftGenerator.GenerateDraft(
            _session.Transcript,
            _session.ActiveEngineType ?? CaptureEngineType.Desktop,
            $"Session {shortId}");
        ScriptEditor.ApplyDraft(script);
    }

    private void StartRecording()
    {
        if (!SelectedEngineType.HasValue) return;
        var opts = new RecordingSessionOptions(SelectedEngineType.Value, System.IO.Path.GetTempPath());
        _session.Start(opts);
        if (SelectedEngineType.Value == CaptureEngineType.Desktop)
            _captureEngine.Start();
        else if (SelectedEngineType.Value == CaptureEngineType.Browser)
            _ = BrowserAdapter.StartAsync();
        if (_voice.IsAvailable)
            _ = _voice.StartAsync();
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        if ((state == SessionState.Stopped || state == SessionState.Idle) && _captureEngine.IsRunning)
            _captureEngine.Stop();
        if ((state == SessionState.Stopped || state == SessionState.Idle) && BrowserAdapter.IsConnected)
            _ = BrowserAdapter.StopAsync();
        if (state == SessionState.Stopped || state == SessionState.Idle)
            _ = _voice.StopAsync();
        OnPropertyChanged(nameof(CurrentState));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsStopped));
        ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
        ((RelayCommand)GenerateDraftCommand).RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        ScriptEditor.PropertyChanged -= OnScriptEditorChanged;
        if (_watchedSegment is not null) _watchedSegment.PropertyChanged -= OnSegmentChanged;
        _session.StateChanged -= OnStateChanged;
        _captureEngine.Dispose();
        TestRunner.Dispose();
        BrowserAdapter.Dispose();
    }
}
