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
    private static readonly ObservableCollection<StepParameterRow> NoParameters = [];
    private static readonly ObservableCollection<DataReference> NoData = [];

    private Script? _script;
    private StepRowViewModel? _selectedRow;
    private ScriptSegment? _selectedSegment;
    private ObservableCollection<ScriptStep>? _watchedSteps;

    public Script? Script
    {
        get => _script;
        set
        {
            if (_script is not null) _script.Segments.CollectionChanged -= OnSegmentsChanged;
            _script = value;
            if (_script is not null) _script.Segments.CollectionChanged += OnSegmentsChanged;
            _selectedSegment = _script?.Segments.FirstOrDefault();
            SelectedRow = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasScript));
            RefreshSegmentState();
        }
    }

    public ScriptStep? SelectedStep
    {
        get => _selectedRow?.Step;
        set => SelectedRow = value is null
            ? null
            : StepRows.FirstOrDefault(r => ReferenceEquals(r.Step, value));
    }

    /// <summary>One row per step, each owning that step's own details.</summary>
    public ObservableCollection<StepRowViewModel> StepRows { get; } = [];

    /// <summary>
    /// The selected row. The step list binds its selection here, and the editor-level members
    /// work through it.
    /// </summary>
    public StepRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            // The editor-level members read through the selected row, so its details have to be
            // built even when the row itself is not open.
            _selectedRow?.Refresh();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedStep));
            RaiseSelectedStepDetails();
            UpdateCommandStates();
        }
    }

    /// <summary>The parameter row an inserted reference is written into.</summary>
    public StepParameterRow? SelectedParameter
    {
        get => SelectedRow?.SelectedParameter;
        set
        {
            if (SelectedRow is null) return;
            SelectedRow.SelectedParameter = value;
            OnPropertyChanged();
            UpdateCommandStates();
        }
    }

    /// <summary>The selected step's parameters, editable as name and value pairs.</summary>
    public ObservableCollection<StepParameterRow> StepParameters => SelectedRow?.StepParameters ?? NoParameters;

    /// <summary>What a later step or segment can read once this step has run.</summary>
    public ObservableCollection<DataReference> AvailableData => SelectedRow?.AvailableData ?? NoData;

    /// <summary>Actions the runtime of the selected segment understands.</summary>
    public IReadOnlyList<string> ActionOptions => ActionCatalog.NamesFor(StepRuntime);

    /// <summary>Parameters the selected action understands but the step does not carry yet.</summary>
    public IReadOnlyList<string> SuggestedParameters => SelectedRow?.SuggestedParameters ?? [];

    /// <summary>
    /// The name the selected step publishes its outputs under. Setting it is how data reaches a
    /// later step, and across a segment boundary a later segment.
    /// </summary>
    public string StoreAs
    {
        get => SelectedRow?.StoreAs ?? string.Empty;
        set
        {
            if (SelectedRow is null) return;
            SelectedRow.StoreAs = value;
            RaiseSelectedStepDetails();
        }
    }

    /// <summary>What the selected step publishes, spelled out as the references to use.</summary>
    public string StoreAsHint => SelectedRow?.StoreAsHint ?? string.Empty;

    public bool HasAvailableData => SelectedRow?.HasAvailableData ?? false;

    private string StepRuntime =>
        _script is null ? "desktop"
        : _script.IsSegmented ? _selectedSegment?.Runtime ?? "desktop"
        : _script.TargetRuntime;

    /// <summary>Rebuilds a row per step, so each row carries its own details.</summary>
    private void RebuildStepRows()
    {
        if (_watchedSteps is not null) _watchedSteps.CollectionChanged -= OnActiveStepsChanged;
        _watchedSteps = ActiveSteps;
        if (_watchedSteps is not null) _watchedSteps.CollectionChanged += OnActiveStepsChanged;

        // Rebuilding replaces every row, so remember what was open and what was selected and
        // put them back; otherwise adding a step would close and deselect everything.
        var wasExpanded = StepRows.Where(r => r.IsExpanded).Select(r => r.Step).ToHashSet();
        var wasSelected = _selectedRow?.Step;
        foreach (var row in StepRows) row.Detach();
        StepRows.Clear();
        _selectedRow = null;

        foreach (var step in ActiveSteps ?? [])
        {
            var row = new StepRowViewModel(step, () => StepRuntime, StepsDataFor, OnRowDataChanged);
            if (wasExpanded.Contains(step)) row.IsExpanded = true;
            StepRows.Add(row);
            if (ReferenceEquals(step, wasSelected)) _selectedRow = row;
        }
        _selectedRow?.Refresh();

        OnPropertyChanged(nameof(StepRows));
        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(SelectedStep));
        RaiseSelectedStepDetails();
    }

    private IReadOnlyList<DataReference> StepsDataFor(ScriptStep step) =>
        _script is null ? [] : DataFlow.AvailableTo(_script, _selectedSegment, step);

    private void OnActiveStepsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildStepRows();

    /// <summary>A storeAs changed, so what the rows after it can read changed with it.</summary>
    private void OnRowDataChanged()
    {
        foreach (var row in StepRows.Where(r => r.IsExpanded)) row.Refresh();
        RaiseSelectedStepDetails();
    }

    private void RaiseSelectedStepDetails()
    {
        OnPropertyChanged(nameof(StepParameters));
        OnPropertyChanged(nameof(AvailableData));
        OnPropertyChanged(nameof(SelectedParameter));
        OnPropertyChanged(nameof(StoreAs));
        OnPropertyChanged(nameof(StoreAsHint));
        OnPropertyChanged(nameof(ActionOptions));
        OnPropertyChanged(nameof(SuggestedParameters));
        OnPropertyChanged(nameof(HasAvailableData));
    }

    public ScriptSegment? SelectedSegment
    {
        get => _selectedSegment;
        set
        {
            _selectedSegment = value;
            _selectedRow = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedStep));
            OnPropertyChanged(nameof(SelectedRow));
            OnPropertyChanged(nameof(ActiveSteps));
            OnPropertyChanged(nameof(HasSelectedSegment));
            RebuildStepRows();
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
    public IReadOnlyList<string> BrowserSessionOptions => ScriptSegment.BrowserSessions;

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
    public ICommand AddParameterCommand   { get; }
    public ICommand DeleteParameterCommand{ get; }
    public ICommand InsertReferenceCommand{ get; }

    public ScriptEditorViewModel()
    {
        AddStepCommand         = new RelayCommand(_ => AddStep(), _ => ActiveSteps is not null);
        DeleteStepCommand      = new RelayCommand(_ => DeleteStep(), _ => _selectedRow is not null);
        MoveUpCommand          = new RelayCommand(_ => MoveUp(),     _ => CanMoveUp());
        MoveDownCommand        = new RelayCommand(_ => MoveDown(),   _ => CanMoveDown());
        // Always available: with no script open yet it starts one, so a new flow can be built
        // straight after the Designer opens.
        AddSegmentCommand      = new RelayCommand(_ => AddSegment());
        // The last segment is not deleted: "Make Single" turns it back into a plain script.
        DeleteSegmentCommand   = new RelayCommand(_ => DeleteSegment(), _ => _selectedSegment is not null && _script is { Segments.Count: > 1 });
        MoveSegmentUpCommand   = new RelayCommand(_ => MoveSegment(-1), _ => CanMoveSegment(-1));
        MoveSegmentDownCommand = new RelayCommand(_ => MoveSegment(1),  _ => CanMoveSegment(1));
        MergeToSingleCommand   = new RelayCommand(_ => MergeToSingle(), _ => _script is { Segments.Count: 1 });
        AddParameterCommand    = new RelayCommand(_ => { SelectedRow?.AddParameter(); RaiseSelectedStepDetails(); },
                                                  _ => _selectedRow is not null);
        DeleteParameterCommand = new RelayCommand(_ => { SelectedRow?.DeleteParameter(); RaiseSelectedStepDetails(); },
                                                  _ => SelectedRow?.SelectedParameter is not null);
        InsertReferenceCommand = new RelayCommand(p => SelectedRow?.InsertReference(p),
                                                  _ => SelectedRow?.SelectedParameter is not null);
    }

    public void LoadScript(Script script)
    {
        Script = script;
        RenumberAll();
    }

    /// <summary>Replaces whatever is open with an empty script, ready for a new flow.</summary>
    public void NewScript()
    {
        Script = new Script();
        SelectedRow = null;
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
        SelectedRow?.Refresh();
    }

    private void DeleteStep()
    {
        var steps = ActiveSteps;
        if (steps is null || SelectedStep is not { } step) return;
        steps.Remove(step);
        SelectedRow = null;
        RenumberAll();
    }

    private bool CanMoveUp() => ActiveSteps is { } s && SelectedStep is { } step && s.IndexOf(step) > 0;
    private bool CanMoveDown() => ActiveSteps is { } s && SelectedStep is { } step && s.IndexOf(step) < s.Count - 1;

    private void MoveUp()
    {
        var steps = ActiveSteps;
        if (steps is null || SelectedStep is not { } step) return;
        int i = steps.IndexOf(step);
        if (i <= 0) return;
        steps.Move(i, i - 1);
        SelectedStep = step;
        RenumberAll();
    }

    private void MoveDown()
    {
        var steps = ActiveSteps;
        if (steps is null || SelectedStep is not { } step) return;
        int i = steps.IndexOf(step);
        if (i < 0 || i >= steps.Count - 1) return;
        steps.Move(i, i + 1);
        SelectedStep = step;
        RenumberAll();
    }

    // ── Segments ────────────────────────────────────────────────────────────

    private void AddSegment()
    {
        // No script open yet: start a blank one so the first segment has somewhere to go.
        if (_script is null) Script = new Script();
        var script = _script!;

        // The first segment of a single-runtime script takes the script's runtime and steps.
        if (!script.IsSegmented)
        {
            var first = new ScriptSegment
            {
                Name = "Segment 1",
                Runtime = ScriptSegment.Runtimes.Contains(script.TargetRuntime) ? script.TargetRuntime : "desktop"
            };
            foreach (var step in script.Steps.ToList()) first.Steps.Add(step);
            script.Steps.Clear();
            script.Segments.Add(first);
            if (first.Steps.Count == 0)
            {
                SelectedSegment = first;
                return;
            }
        }

        var segment = new ScriptSegment
        {
            Name = $"Segment {script.Segments.Count + 1}",
            // A different runtime from the previous segment is the usual reason to add one.
            Runtime = script.Segments.LastOrDefault()?.Runtime == "desktop" ? "browser" : "desktop"
        };
        var at = _selectedSegment is null ? script.Segments.Count : script.Segments.IndexOf(_selectedSegment) + 1;
        script.Segments.Insert(at, segment);
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
        RebuildStepRows();
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
        ((RelayCommand)AddParameterCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteParameterCommand).RaiseCanExecuteChanged();
        ((RelayCommand)InsertReferenceCommand).RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
