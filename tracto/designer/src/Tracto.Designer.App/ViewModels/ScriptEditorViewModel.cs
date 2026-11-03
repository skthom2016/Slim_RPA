namespace Tracto.Designer.App.ViewModels;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Scripting;

public sealed class ScriptEditorViewModel : INotifyPropertyChanged
{
    private Script? _script;
    private ScriptStep? _selectedStep;
    private ScriptSegment? _selectedSegment;

    public Script? Script
    {
        get => _script;
        set
        {
            if (_script is not null) _script.Segments.CollectionChanged -= OnSegmentsChanged;
            _script = value;
            if (_script is not null) _script.Segments.CollectionChanged += OnSegmentsChanged;
            _selectedStep = null;
            _selectedSegment = _script?.Segments.FirstOrDefault();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasScript));
            OnPropertyChanged(nameof(SelectedStep));
            RefreshSegmentState();
        }
    }

    public ScriptStep? SelectedStep
    {
        get => _selectedStep;
        set { _selectedStep = value; OnPropertyChanged(); UpdateCommandStates(); }
    }

    public ScriptSegment? SelectedSegment
    {
        get => _selectedSegment;
        set
        {
            _selectedSegment = value;
            _selectedStep = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedStep));
            OnPropertyChanged(nameof(ActiveSteps));
            OnPropertyChanged(nameof(HasSelectedSegment));
            UpdateCommandStates();
        }
    }

    public bool HasScript => _script is not null;
    public bool IsSegmented => _script?.IsSegmented ?? false;
    public bool HasSelectedSegment => _selectedSegment is not null;

    /// <summary>The steps the step list shows: the selected segment's, or the script's own.</summary>
    public ObservableCollection<ScriptStep>? ActiveSteps =>
        _script is null ? null
        : _script.IsSegmented ? _selectedSegment?.Steps
        : _script.Steps;

    public IReadOnlyList<string> RuntimeOptions => ScriptSegment.Runtimes;
    public IReadOnlyList<string> ModeOptions => ScriptSegment.Modes;

    /// <summary>Default parameters as "name=value" lines. Steps read them as ${param.name}.</summary>
    public string ParamsText
    {
        get => _script is null ? "" : string.Join(Environment.NewLine, _script.Params.Select(kv => $"{kv.Key}={kv.Value}"));
        set
        {
            if (_script is null) return;
            var parsed = new Dictionary<string, string>();
            foreach (var line in value.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var text = line.TrimEnd('\r');
                var eq = text.IndexOf('=');
                if (eq <= 0) continue;
                parsed[text[..eq].Trim()] = text[(eq + 1)..];
            }
            _script.Params = parsed;
            OnPropertyChanged();
        }
    }

    public ICommand AddStepCommand        { get; }
    public ICommand DeleteStepCommand     { get; }
    public ICommand MoveUpCommand         { get; }
    public ICommand MoveDownCommand       { get; }
    public ICommand AddSegmentCommand     { get; }
    public ICommand DeleteSegmentCommand  { get; }
    public ICommand MoveSegmentUpCommand  { get; }
    public ICommand MoveSegmentDownCommand{ get; }
    public ICommand MergeToSingleCommand  { get; }

    public ScriptEditorViewModel()
    {
        AddStepCommand         = new RelayCommand(_ => AddStep(), _ => ActiveSteps is not null);
        DeleteStepCommand      = new RelayCommand(_ => DeleteStep(), _ => SelectedStep is not null);
        MoveUpCommand          = new RelayCommand(_ => MoveUp(),     _ => CanMoveUp());
        MoveDownCommand        = new RelayCommand(_ => MoveDown(),   _ => CanMoveDown());
        AddSegmentCommand      = new RelayCommand(_ => AddSegment(), _ => _script is not null);
        // The last segment is not deleted: "Make Single" turns it back into a plain script.
        DeleteSegmentCommand   = new RelayCommand(_ => DeleteSegment(), _ => _selectedSegment is not null && _script is { Segments.Count: > 1 });
        MoveSegmentUpCommand   = new RelayCommand(_ => MoveSegment(-1), _ => CanMoveSegment(-1));
        MoveSegmentDownCommand = new RelayCommand(_ => MoveSegment(1),  _ => CanMoveSegment(1));
        MergeToSingleCommand   = new RelayCommand(_ => MergeToSingle(), _ => _script is { Segments.Count: 1 });
    }

    public void LoadScript(Script script)
    {
        Script = script;
        RenumberAll();
    }

    /// <summary>
    /// Puts recorded steps into the editor. A single-runtime script is replaced. In a segmented
    /// script an empty selected segment is filled; a segment that already has steps is kept and
    /// the recording becomes a new segment after it.
    /// </summary>
    public void ApplyDraft(Script draft)
    {
        if (_script is null || !_script.IsSegmented)
        {
            LoadScript(draft);
            return;
        }

        var target = _selectedSegment;
        if (target is null || target.Steps.Count > 0)
        {
            target = new ScriptSegment { Name = $"Segment {_script.Segments.Count + 1}" };
            var at = _selectedSegment is null ? _script.Segments.Count : _script.Segments.IndexOf(_selectedSegment) + 1;
            _script.Segments.Insert(at, target);
        }
        target.Runtime = draft.TargetRuntime;
        foreach (var step in draft.Steps) target.Steps.Add(step);
        SelectedSegment = target;
        RenumberAll();
    }

    private void AddStep()
    {
        var steps = ActiveSteps;
        if (steps is null) return;
        var step = new ScriptStep { StepNumber = steps.Count + 1, Description = "New step", Action = "action" };
        steps.Add(step);
        SelectedStep = step;
    }

    private void DeleteStep()
    {
        var steps = ActiveSteps;
        if (steps is null || _selectedStep is null) return;
        steps.Remove(_selectedStep);
        SelectedStep = null;
        RenumberAll();
    }

    private bool CanMoveUp() => ActiveSteps is { } s && _selectedStep is not null && s.IndexOf(_selectedStep) > 0;
    private bool CanMoveDown() => ActiveSteps is { } s && _selectedStep is not null && s.IndexOf(_selectedStep) < s.Count - 1;

    private void MoveUp()
    {
        var steps = ActiveSteps;
        if (steps is null || _selectedStep is null) return;
        int i = steps.IndexOf(_selectedStep);
        if (i <= 0) return;
        steps.Move(i, i - 1);
        RenumberAll();
    }

    private void MoveDown()
    {
        var steps = ActiveSteps;
        if (steps is null || _selectedStep is null) return;
        int i = steps.IndexOf(_selectedStep);
        if (i < 0 || i >= steps.Count - 1) return;
        steps.Move(i, i + 1);
        RenumberAll();
    }

    // ── Segments ────────────────────────────────────────────────────────────

    private void AddSegment()
    {
        if (_script is null) return;

        // The first segment of a single-runtime script takes the script's runtime and steps.
        if (!_script.IsSegmented)
        {
            var first = new ScriptSegment
            {
                Name = "Segment 1",
                Runtime = ScriptSegment.Runtimes.Contains(_script.TargetRuntime) ? _script.TargetRuntime : "desktop"
            };
            foreach (var step in _script.Steps.ToList()) first.Steps.Add(step);
            _script.Steps.Clear();
            _script.Segments.Add(first);
            if (first.Steps.Count == 0)
            {
                SelectedSegment = first;
                return;
            }
        }

        var segment = new ScriptSegment
        {
            Name = $"Segment {_script.Segments.Count + 1}",
            // A different runtime from the previous segment is the usual reason to add one.
            Runtime = _script.Segments.LastOrDefault()?.Runtime == "desktop" ? "browser" : "desktop"
        };
        var at = _selectedSegment is null ? _script.Segments.Count : _script.Segments.IndexOf(_selectedSegment) + 1;
        _script.Segments.Insert(at, segment);
        SelectedSegment = segment;
    }

    private void DeleteSegment()
    {
        if (_script is null || _selectedSegment is null || _script.Segments.Count <= 1) return;
        var i = _script.Segments.IndexOf(_selectedSegment);
        _script.Segments.Remove(_selectedSegment);
        SelectedSegment = _script.Segments.Count == 0 ? null : _script.Segments[Math.Min(i, _script.Segments.Count - 1)];
    }

    private bool CanMoveSegment(int by)
    {
        if (_script is null || _selectedSegment is null) return false;
        var i = _script.Segments.IndexOf(_selectedSegment) + by;
        return i >= 0 && i < _script.Segments.Count;
    }

    private void MoveSegment(int by)
    {
        if (!CanMoveSegment(by) || _script is null || _selectedSegment is null) return;
        var segment = _selectedSegment;
        var i = _script.Segments.IndexOf(segment);
        _script.Segments.Move(i, i + by);
        SelectedSegment = segment;
    }

    /// <summary>Turns a script with one segment back into a single-runtime script.</summary>
    private void MergeToSingle()
    {
        if (_script is not { Segments.Count: 1 }) return;
        var only = _script.Segments[0];
        _script.TargetRuntime = only.Runtime;
        foreach (var step in only.Steps.ToList()) _script.Steps.Add(step);
        _script.Segments.Clear();
        RenumberAll();
    }

    private void OnSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_selectedSegment is not null && _script is not null && !_script.Segments.Contains(_selectedSegment))
            _selectedSegment = _script.Segments.FirstOrDefault();
        RefreshSegmentState();
    }

    private void RefreshSegmentState()
    {
        OnPropertyChanged(nameof(IsSegmented));
        OnPropertyChanged(nameof(SelectedSegment));
        OnPropertyChanged(nameof(HasSelectedSegment));
        OnPropertyChanged(nameof(ActiveSteps));
        OnPropertyChanged(nameof(ParamsText));
        UpdateCommandStates();
    }

    private void RenumberAll()
    {
        if (_script is null) return;
        Renumber(_script.Steps);
        foreach (var segment in _script.Segments) Renumber(segment.Steps);
    }

    private static void Renumber(IList<ScriptStep> steps)
    {
        for (int i = 0; i < steps.Count; i++)
            steps[i].StepNumber = i + 1;
    }

    private void UpdateCommandStates()
    {
        ((RelayCommand)AddStepCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteStepCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AddSegmentCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteSegmentCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveSegmentUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveSegmentDownCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MergeToSingleCommand).RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
