namespace Tracto.Designer.App.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.App.Services;
using Tracto.Designer.Core.Templates;

public sealed class TemplateLibraryViewModel : INotifyPropertyChanged
{
    private readonly ITemplateLibrary _library;
    private readonly ScriptEditorViewModel _editor;
    private readonly IDialogService _dialog;
    private string _searchText = "";
    private TemplateCategory? _categoryFilter;
    private ScriptTemplate? _selected;

    public TemplateLibraryViewModel(
        ITemplateLibrary library,
        ScriptEditorViewModel editor,
        IDialogService dialog)
    {
        _library = library;
        _editor = editor;
        _dialog = dialog;
        UseTemplateCommand = new RelayCommand(_ => UseTemplate(), _ => _selected is not null);
        Categories = Enum.GetValues<TemplateCategory>();
        Refresh();
    }

    public ObservableCollection<ScriptTemplate> Templates { get; } = [];
    public Array Categories { get; }

    public ScriptTemplate? SelectedTemplate
    {
        get => _selected;
        set
        {
            _selected = value;
            OnPropertyChanged();
            ((RelayCommand)UseTemplateCommand).RaiseCanExecuteChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); Refresh(); }
    }

    public TemplateCategory? CategoryFilter
    {
        get => _categoryFilter;
        set { _categoryFilter = value; OnPropertyChanged(); Refresh(); }
    }

    public ICommand UseTemplateCommand { get; }

    private void Refresh()
    {
        var results = string.IsNullOrWhiteSpace(_searchText)
            ? _library.GetAll()
            : _library.Search(_searchText);
        if (_categoryFilter.HasValue)
            results = [..results.Where(t => t.Category == _categoryFilter.Value)];
        Templates.Clear();
        foreach (var t in results) Templates.Add(t);
    }

    private void UseTemplate()
    {
        if (_selected is null) return;
        if (_editor.Script is { Steps.Count: > 0 })
        {
            if (!_dialog.Confirm(
                "Replace current script with template? Unsaved steps will be lost.",
                "Use Template"))
                return;
        }
        _editor.Script = _selected.CreateScript();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
