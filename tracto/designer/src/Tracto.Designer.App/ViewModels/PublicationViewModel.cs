namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Publication;
using Tracto.Designer.Core.Testing;

public sealed class PublicationViewModel : INotifyPropertyChanged
{
    private readonly IScriptPublisher _publisher;
    private readonly ScriptEditorViewModel _editor;
    private readonly TestPanelViewModel _testing;
    private readonly bool _testRequired;
    private readonly OutputLogViewModel? _output;
    private PublicationResult? _result;
    private bool _isPublishing;
    private bool _setAsLatest;
    private string _outputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tracto", "Published");

    /// <param name="publisher">The script publisher implementation.</param>
    /// <param name="editor">View-model that owns the current script draft.</param>
    /// <param name="testing">View-model that tracks the last test result.</param>
    /// <param name="testRequired">
    /// When <c>true</c> (the default) the Publish button stays disabled until a passing test
    /// result is present.  When <c>false</c>, deployment policy does not mandate a live test
    /// and the UI permits publishing from the VALIDATED state (DS-16-AC01).
    /// </param>
    /// <param name="output">Where publication progress and its outcome are reported.</param>
    public PublicationViewModel(IScriptPublisher publisher, ScriptEditorViewModel editor,
        TestPanelViewModel testing, bool testRequired = true,
        OutputLogViewModel? output = null)
    {
        _output = output;
        _publisher = publisher;
        _editor = editor;
        _testing = testing;
        _testRequired = testRequired;
        PublishCommand = new RelayCommand(async _ => await PublishAsync(), _ => CanPublish());
    }

    public ICommand PublishCommand { get; }

    /// <summary>
    /// Also move the "latest" tag to the version being published. Starts off, and goes back to off
    /// after each successful publish, so moving the tag is a choice made each time.
    /// </summary>
    public bool SetAsLatest
    {
        get => _setAsLatest;
        set { _setAsLatest = value; OnPropertyChanged(); }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set { _outputDirectory = value; OnPropertyChanged(); }
    }

    public bool IsPublishing
    {
        get => _isPublishing;
        private set
        {
            _isPublishing = value;
            OnPropertyChanged();
            ((RelayCommand)PublishCommand).RaiseCanExecuteChanged();
        }
    }

    public PublicationResult? LastResult
    {
        get => _result;
        private set
        {
            _result = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasResult));
        }
    }

    public bool HasResult => _result is not null;
    public string PublisherName => _publisher.Name;

    private bool CanPublish() => !_isPublishing && _editor.Script is not null
        && (!_testRequired || (_testing.HasResult && _testing.Passed));

    private async Task PublishAsync()
    {
        if (_editor.Script is null) return;
        // When testing is not required by policy, substitute a "not-run" sentinel so the
        // gate (which also honours _testRequired) does not block the request.
        if (_testRequired && _testing.TestResult is null) return;
        IsPublishing = true;
        _output?.Info("Publish", $"Publishing {_editor.Script.Name} to {OutputDirectory} with {_publisher.Name}...");
        try
        {
            var testResult = _testing.TestResult ?? ScriptTestResult.NotRun;
            var req = new PublicationRequest(_editor.Script, testResult, OutputDirectory, SetAsLatest);
            var result = await _publisher.PublishAsync(req).ConfigureAwait(false);
            LastResult = result;
            if (result.Success && result.AlreadyPublished)
            {
                // Same version, same content: the Orchestrator already has it, so nothing was added.
                _output?.Warning("Publish",
                    $"Nothing new was published: version {req.Script.Version} is already on the Orchestrator with identical content. " +
                    "To publish a change, raise the version.");
                if (result.MovedLatest)
                    _output?.Success("Publish", $"'latest' now points at version {req.Script.Version}.");
                SetAsLatest = false;
            }
            else if (result.Success)
            {
                _output?.Success("Publish", $"Published — ID {result.PublicationId}");
                if (result.MovedLatest)
                    _output?.Success("Publish", $"'latest' now points at version {req.Script.Version}.");
                SetAsLatest = false;
            }
            else
                _output?.Error("Publish", $"FAILED: {result.Message}");
        }
        finally
        {
            IsPublishing = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
