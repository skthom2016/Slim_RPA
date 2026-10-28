namespace Tracto.Designer.App.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Audit;

public sealed class AuditViewModel : INotifyPropertyChanged
{
    private readonly AuditService _audit;
    private AuditCategory? _categoryFilter;

    public ObservableCollection<AuditEntry> DisplayedEntries { get; } = [];
    public ICommand RefreshCommand { get; }
    public Array Categories { get; } = Enum.GetValues<AuditCategory>();

    public AuditCategory? CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            _categoryFilter = value;
            OnPropertyChanged();
            RefreshEntries();
        }
    }

    public AuditViewModel(AuditService audit)
    {
        _audit = audit;
        RefreshCommand = new RelayCommand(_ => RefreshEntries());
        RefreshEntries();
    }

    public void RefreshEntries()
    {
        var entries = _audit.GetEntries(_categoryFilter)
            .OrderByDescending(e => e.Timestamp)
            .Take(50);
        DisplayedEntries.Clear();
        foreach (var e in entries)
            DisplayedEntries.Add(e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
