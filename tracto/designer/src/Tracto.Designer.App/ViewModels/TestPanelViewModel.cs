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
    private readonly OutputLogViewModel? _output;
    private CancellationTokenSource? _cts;
    private ScriptTestResult? _result;
    private bool _isRunning;
    private int _progress;

    public TestPanelViewModel(IScriptTestRunner runner, ScriptEditorViewModel editor, OutputLogViewModel? output = null)
    {
        _runner = runner;
        _editor = editor;
        _output = output;
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
        _output?.Info("Test", $"Running {_editor.Script.Name} with {_runner.Name}...");
        try
        {
            var prog = new Progress<int>(p => ProgressPercent = p);
            var result = await _runner.RunAsync(_editor.Script, prog, _cts.Token).ConfigureAwait(false);
            TestResult = result;
            Report(result);
        }
        catch (OperationCanceledException)
        {
            TestResult = new ScriptTestResult(false, [], TimeSpan.Zero, "Test run cancelled.");
            _output?.Warning("Test", "Test run cancelled.");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Report(ScriptTestResult result)
    {
        if (_output is null) return;

        foreach (var step in result.StepResults)
            _output.Write("Test", step.Passed ? OutputSeverity.Info : OutputSeverity.Error,
                $"Step {step.StepNumber}: {step.Message} ({Took(step.Duration)})");

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            _output.Error("Test", result.ErrorMessage!);

        var steps = result.StepResults.Count == 1 ? "1 step" : $"{result.StepResults.Count} steps";
        if (result.Passed)
            _output.Success("Test", $"PASSED — {steps} in {Took(result.Duration)}.");
        else
            _output.Error("Test", $"FAILED after {Took(result.Duration)}.");
    }

    /// <summary>A duration an author can read at a glance, rather than a raw TimeSpan.</summary>
    private static string Took(TimeSpan d) =>
        d.TotalSeconds < 1   ? $"{d.TotalMilliseconds:F0} ms"
        : d.TotalMinutes < 1 ? $"{d.TotalSeconds:F1} s"
                             : $"{(int)d.TotalMinutes}m {d.Seconds}s";

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
