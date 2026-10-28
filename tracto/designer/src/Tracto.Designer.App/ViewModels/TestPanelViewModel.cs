namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Testing;

public sealed class TestPanelViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IScriptTestRunner _runner;
    private readonly ScriptEditorViewModel _editor;
    private CancellationTokenSource? _cts;
    private ScriptTestResult? _result;
    private bool _isRunning;
    private int _progress;

    public TestPanelViewModel(IScriptTestRunner runner, ScriptEditorViewModel editor)
    {
        _runner = runner;
        _editor = editor;
        RunTestCommand  = new RelayCommand(async _ => await RunTestAsync(), _ => !_isRunning && _editor.Script is not null);
        StopTestCommand = new RelayCommand(_ => _cts?.Cancel(), _ => _isRunning);
    }

    public string RunnerName => _runner.Name;

    public ICommand RunTestCommand  { get; }
    public ICommand StopTestCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set { _isRunning = value; OnPropertyChanged(); RaiseAll(); }
    }

    public int ProgressPercent
    {
        get => _progress;
        private set { _progress = value; OnPropertyChanged(); }
    }

    public ScriptTestResult? TestResult
    {
        get => _result;
        private set
        {
            _result = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(Passed));
        }
    }

    public bool HasResult => _result is not null;
    public bool Passed    => _result?.Passed ?? false;

    private async Task RunTestAsync()
    {
        if (_editor.Script is null) return;
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProgressPercent = 0;
        TestResult = null;
        try
        {
            var prog = new Progress<int>(p => ProgressPercent = p);
            TestResult = await _runner.RunAsync(_editor.Script, prog, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TestResult = new ScriptTestResult(false, [], TimeSpan.Zero, "Test run cancelled.");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void RaiseAll()
    {
        ((RelayCommand)RunTestCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopTestCommand).RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
