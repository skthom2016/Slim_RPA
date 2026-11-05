namespace Tracto.Designer.App.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Scripting;

/// <summary>
/// One row of the step list, and everything that belongs to that step: its description, the
/// action it runs, its parameters and the data it may read. Keeping them on the row lets a step
/// be opened where it sits, instead of reading its details in a panel far from the list.
/// </summary>
public sealed class StepRowViewModel : INotifyPropertyChanged
{
    private readonly Func<string> _runtime;
    private readonly Func<ScriptStep, IReadOnlyList<DataReference>> _availableData;
    private readonly Action _dataChanged;
    private StepParameterRow? _selectedParameter;
    private IReadOnlyList<string>? _actionOptions;
    private IReadOnlyList<string>? _parameterNameOptions;
    private bool _isExpanded;
    private bool _isEditingDescription;
    private string _descriptionDraft = string.Empty;

    public StepRowViewModel(ScriptStep step, Func<string> runtime,
        Func<ScriptStep, IReadOnlyList<DataReference>> availableData, Action dataChanged)
    {
        Step = step;
        _runtime = runtime;
        _availableData = availableData;
        _dataChanged = dataChanged;
        step.PropertyChanged += OnStepChanged;

        ToggleExpandCommand      = new RelayCommand(_ => IsExpanded = !IsExpanded);
        EditDescriptionCommand   = new RelayCommand(_ => BeginEditDescription(), _ => !_isEditingDescription);
        SaveDescriptionCommand   = new RelayCommand(_ => SaveDescription(),      _ => _isEditingDescription);
        CancelDescriptionCommand = new RelayCommand(_ => CancelDescription(),    _ => _isEditingDescription);
        AddParameterCommand      = new RelayCommand(_ => AddParameter());
        DeleteParameterCommand   = new RelayCommand(_ => DeleteParameter(), _ => _selectedParameter is not null);
        InsertReferenceCommand   = new RelayCommand(InsertReference,        _ => _selectedParameter is not null);
    }

    public ScriptStep Step { get; }

    public int StepNumber => Step.StepNumber;

    /// <summary>The description as the list shows it when it is not being edited.</summary>
    public string Description => Step.Description;

    public string Action
    {
        get => Step.Action;
        set { Step.Action = value ?? string.Empty; }
    }

    public ICommand ToggleExpandCommand      { get; }
    public ICommand EditDescriptionCommand   { get; }
    public ICommand SaveDescriptionCommand   { get; }
    public ICommand CancelDescriptionCommand { get; }
    public ICommand AddParameterCommand      { get; }
    public ICommand DeleteParameterCommand   { get; }
    public ICommand InsertReferenceCommand   { get; }

    /// <summary>
    /// Open rows show their action, parameters and the data they can read. The details are
    /// built when the row opens, so a long script does not build what nobody is looking at.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            if (value) Refresh();
            OnPropertyChanged();
        }
    }

    // ── the description, edited only after the pencil is pressed ─────────────

    /// <summary>
    /// A description is only editable once the pencil is pressed. Selecting a step used to put
    /// the caret straight into its description, so a stray click could change it unnoticed.
    /// </summary>
    public bool IsEditingDescription
    {
        get => _isEditingDescription;
        private set
        {
            _isEditingDescription = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsShowingDescription));
            ((RelayCommand)EditDescriptionCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SaveDescriptionCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelDescriptionCommand).RaiseCanExecuteChanged();
        }
    }

    public bool IsShowingDescription => !_isEditingDescription;

    /// <summary>What the edit box holds. It reaches the step only when the tick is pressed.</summary>
    public string DescriptionDraft
    {
        get => _descriptionDraft;
        set { _descriptionDraft = value ?? string.Empty; OnPropertyChanged(); }
    }

    private void BeginEditDescription()
    {
        DescriptionDraft = Step.Description;
        IsEditingDescription = true;
    }

    private void SaveDescription()
    {
        Step.Description = _descriptionDraft.Trim();
        IsEditingDescription = false;
        OnPropertyChanged(nameof(Description));
    }

    private void CancelDescription()
    {
        DescriptionDraft = Step.Description;
        IsEditingDescription = false;
    }

    // ── action, parameters and the data this step can read ──────────────────

    public ObservableCollection<StepParameterRow> StepParameters { get; } = [];
    public ObservableCollection<DataReference> AvailableData { get; } = [];

    public StepParameterRow? SelectedParameter
    {
        get => _selectedParameter;
        set
        {
            _selectedParameter = value;
            OnPropertyChanged();
            ((RelayCommand)DeleteParameterCommand).RaiseCanExecuteChanged();
            ((RelayCommand)InsertReferenceCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Actions the runtime of this segment understands. Held rather than rebuilt on each read:
    /// handing the editable action box a new list resets it and blanks the action it shows.
    /// </summary>
    public IReadOnlyList<string> ActionOptions =>
        _actionOptions ??= ActionCatalog.NamesFor(_runtime());

    public IReadOnlyList<string> SuggestedParameters
    {
        get
        {
            var known = ActionCatalog.Find(_runtime(), Step.Action)?.AllParameters ?? [];
            return [.. known.Concat([ActionCatalog.StoreAs])
                            .Where(p => !Step.Parameters.ContainsKey(p))
                            .Distinct(StringComparer.Ordinal)];
        }
    }

    /// <summary>
    /// What the name box offers: every parameter this action takes that no other row already
    /// holds, required ones first. Typing a name outside the list is still allowed, because the
    /// catalogue does not know every action a runtime may grow.
    /// </summary>
    public IReadOnlyList<string> ParameterNameOptions =>
        _parameterNameOptions ??= BuildParameterNameOptions();

    private IReadOnlyList<string> BuildParameterNameOptions()
    {
        var definition = ActionCatalog.Find(_runtime(), Step.Action);
        if (definition is null) return [ActionCatalog.StoreAs];

        return [.. definition.AllParameters
                    .Concat([ActionCatalog.StoreAs])
                    .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>What is wrong with this step, as Validate would report it.</summary>
    public IReadOnlyList<StepIssue> Issues => StepRules.Check(_runtime(), Step);

    public bool HasIssues => Issues.Count > 0;

    /// <summary>The problems with this step, one per line, for the warning under the parameters.</summary>
    public string IssueSummary => string.Join(Environment.NewLine, Issues.Select(i => i.Message));

    /// <summary>
    /// The name this step publishes its outputs under. Setting it is how data reaches a later
    /// step, and across a segment boundary a later segment.
    /// </summary>
    public string StoreAs
    {
        get => DataFlow.StoreAsOf(Step) ?? string.Empty;
        set
        {
            var next = value?.Trim() ?? string.Empty;
            if (next == StoreAs) return;
            if (next.Length == 0) Step.Parameters.Remove(ActionCatalog.StoreAs);
            else Step.Parameters[ActionCatalog.StoreAs] = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StoreAsHint));
            OnPropertyChanged(nameof(SuggestedParameters));
            // Later steps can now read something new, or have lost what they read.
            _dataChanged();
        }
    }

    public string StoreAsHint
    {
        get
        {
            var outputs = ActionCatalog.OutputsOf(_runtime(), Step.Action);
            if (outputs.Count == 0) return "This action produces no data of its own.";
            var name = DataFlow.StoreAsOf(Step);
            if (name is null)
                return "Name it to publish " + string.Join(", ", outputs) + " to later steps and segments.";
            return "Later segments read it as "
                 + string.Join(", ", outputs.Select(o => "${param." + name + "." + o + "}"));
        }
    }

    public bool HasAvailableData => AvailableData.Count > 0;

    /// <summary>
    /// Refreshes only what this step can read. Safe to call while a parameter is being typed,
    /// because it leaves the parameter rows, and the caret in them, alone.
    /// </summary>
    public void RefreshAvailableData()
    {
        AvailableData.Clear();
        foreach (var d in _availableData(Step)) AvailableData.Add(d);
        OnPropertyChanged(nameof(HasAvailableData));
    }

    /// <summary>Rebuilds the parameter rows and the list of data this step can read.</summary>
    public void Refresh()
    {
        StepParameters.Clear();
        foreach (var name in Step.Parameters.Keys.ToList())
            StepParameters.Add(new StepParameterRow(Step.Parameters, name, OnParameterEdited));
        _selectedParameter = null;
        _parameterNameOptions = null;

        AvailableData.Clear();
        foreach (var d in _availableData(Step)) AvailableData.Add(d);

        OnPropertyChanged(nameof(SelectedParameter));
        OnPropertyChanged(nameof(StoreAs));
        OnPropertyChanged(nameof(StoreAsHint));
        OnPropertyChanged(nameof(ActionOptions));
        OnPropertyChanged(nameof(SuggestedParameters));
        OnPropertyChanged(nameof(ParameterNameOptions));
        OnPropertyChanged(nameof(HasAvailableData));
        ((RelayCommand)DeleteParameterCommand).RaiseCanExecuteChanged();
        ((RelayCommand)InsertReferenceCommand).RaiseCanExecuteChanged();
    }

    /// <summary>
    /// A parameter was typed into. Only the wording around it is refreshed: rebuilding the rows
    /// here would destroy the box being typed in and lose the caret after a single key.
    /// </summary>
    private void OnParameterEdited(bool nameChanged)
    {
        OnPropertyChanged(nameof(StoreAsHint));
        OnPropertyChanged(nameof(SuggestedParameters));
        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IssueSummary));
        _ = nameChanged;   // the list no longer depends on which names are in use
    }

    /// <summary>
    /// Adds an empty row. The name is left for the author to pick from the list of parameters
    /// the action understands, rather than guessed at, and nothing reaches the step until one
    /// is chosen.
    /// </summary>
    public void AddParameter()
    {
        var row = new StepParameterRow(Step.Parameters, string.Empty, OnParameterEdited);
        StepParameters.Add(row);
        SelectedParameter = row;
        // The new row has no name yet, so nothing has left the list of names on offer.
        OnParameterEdited(nameChanged: false);
    }

    public void DeleteParameter()
    {
        if (_selectedParameter is null) return;
        Step.Parameters.Remove(_selectedParameter.Name);
        StepParameters.Remove(_selectedParameter);
        SelectedParameter = null;
        OnParameterEdited(nameChanged: true);
    }

    /// <summary>
    /// Writes a reference into the selected parameter, so data is handed on by picking it
    /// rather than by retyping the name of something produced in another segment.
    /// </summary>
    public void InsertReference(object? parameter)
    {
        if (_selectedParameter is null || parameter is not DataReference reference) return;
        _selectedParameter.Value = (_selectedParameter.Value ?? string.Empty) + reference.Reference;
    }

    private void OnStepChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ScriptStep.StepNumber):
                OnPropertyChanged(nameof(StepNumber));
                break;
            case nameof(ScriptStep.Description):
                OnPropertyChanged(nameof(Description));
                break;
            case nameof(ScriptStep.Action):
                _actionOptions = null;
                _parameterNameOptions = null;
                OnPropertyChanged(nameof(ActionOptions));
                OnPropertyChanged(nameof(ParameterNameOptions));
                OnPropertyChanged(nameof(Issues));
                OnPropertyChanged(nameof(HasIssues));
                OnPropertyChanged(nameof(IssueSummary));
                OnPropertyChanged(nameof(Action));
                OnPropertyChanged(nameof(SuggestedParameters));
                OnPropertyChanged(nameof(StoreAsHint));
                break;
        }
    }

    /// <summary>Stops listening to the step once the row is replaced.</summary>
    public void Detach() => Step.PropertyChanged -= OnStepChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
