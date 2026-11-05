namespace Tracto.Designer.App.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;

public enum OutputSeverity { Info, Success, Warning, Error }

/// <summary>One line of the output pane.</summary>
public sealed class OutputEntry(string source, OutputSeverity severity, string message)
{
    public DateTime Time { get; } = DateTime.Now;
    public string Source { get; } = source;
    public OutputSeverity Severity { get; } = severity;
    public string Message { get; } = message;

    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>
/// Everything validation, testing, signing and publishing have to say, in one list in the order
/// it happened. Each of those used to have a panel of its own, so a result was only visible after
/// switching to the right tab; appended here it is visible wherever the author already is.
/// </summary>
public sealed class OutputLogViewModel : INotifyPropertyChanged
{
    /// <summary>Oldest lines are dropped past this, so a long session cannot grow without bound.</summary>
    public const int MaxEntries = 500;

    public OutputLogViewModel()
    {
        ClearCommand = new RelayCommand(_ => Clear(), _ => Entries.Count > 0);
    }

    public ObservableCollection<OutputEntry> Entries { get; } = [];
    public ICommand ClearCommand { get; }

    public bool IsEmpty => Entries.Count == 0;

    public void Write(string source, OutputSeverity severity, string message)
    {
        // Results arrive from background work (a test run, a publish), and an ObservableCollection
        // may only be changed on the thread that owns the view bound to it.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Write(source, severity, message));
            return;
        }

        Entries.Add(new OutputEntry(source, severity, message));
        while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
        AfterChange();
    }

    public void Info   (string source, string message) => Write(source, OutputSeverity.Info,    message);
    public void Success(string source, string message) => Write(source, OutputSeverity.Success, message);
    public void Warning(string source, string message) => Write(source, OutputSeverity.Warning, message);
    public void Error  (string source, string message) => Write(source, OutputSeverity.Error,   message);

    public void Clear()
    {
        Entries.Clear();
        AfterChange();
    }

    private void AfterChange()
    {
        OnPropertyChanged(nameof(IsEmpty));
        ((RelayCommand)ClearCommand).RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
