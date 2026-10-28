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
    private readonly SigningViewModel _signing;
    private readonly TestPanelViewModel _testing;
    private readonly bool _testRequired;
    private PublicationResult? _result;
    private bool _isPublishing;
    private string _outputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tracto", "Published");

    /// <param name="publisher">The script publisher implementation.</param>
    /// <param name="editor">View-model that owns the current script draft.</param>
    /// <param name="signing">View-model that tracks the current signature.</param>
    /// <param name="testing">View-model that tracks the last test result.</param>
    /// <param name="testRequired">
    /// When <c>true</c> (the default) the Publish button stays disabled until a passing test
    /// result is present.  When <c>false</c>, deployment policy does not mandate a live test
    /// and the UI permits publishing from the VALIDATED state (DS-16-AC01).
    /// </param>
    public PublicationViewModel(IScriptPublisher publisher, ScriptEditorViewModel editor,
        SigningViewModel signing, TestPanelViewModel testing, bool testRequired = true)
    {
        _publisher = publisher;
        _editor = editor;
        _signing = signing;
        _testing = testing;
        _testRequired = testRequired;
        PublishCommand = new RelayCommand(async _ => await PublishAsync(), _ => CanPublish());
    }

    public ICommand PublishCommand { get; }

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
        && _signing.IsSigned
        && (!_testRequired || (_testing.HasResult && _testing.Passed));

    private async Task PublishAsync()
    {
        if (_editor.Script is null || _signing.CurrentSignature is null) return;
        // When testing is not required by policy, substitute a "not-run" sentinel so the
        // gate (which also honours _testRequired) does not block the request.
        if (_testRequired && _testing.TestResult is null) return;
        IsPublishing = true;
        try
        {
            var testResult = _testing.TestResult ?? ScriptTestResult.NotRun;
            var req = new PublicationRequest(_editor.Script, _signing.CurrentSignature, testResult, OutputDirectory);
            LastResult = await _publisher.PublishAsync(req).ConfigureAwait(false);
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
