namespace Tracto.Designer.App.Views;

using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Controls;
using Tracto.Designer.App.ViewModels;

public partial class OutputPanel : UserControl
{
    private INotifyCollectionChanged? _watched;

    public OutputPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch();
        Loaded += (_, _) => Watch();
        Unloaded += (_, _) => Unwatch();
    }

    /// <summary>
    /// The newest line is the one worth reading, so the pane follows it. Without this the author
    /// has to scroll down after every run to see what happened.
    /// </summary>
    private void Watch()
    {
        Unwatch();
        if (DataContext is not RecordingViewModel vm) return;
        _watched = vm.Output.Entries;
        _watched.CollectionChanged += OnEntriesChanged;
    }

    private void Unwatch()
    {
        if (_watched is null) return;
        _watched.CollectionChanged -= OnEntriesChanged;
        _watched = null;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        // Layout has not run yet when the item is added, so scrolling now would stop short.
        Dispatcher.BeginInvoke(new Action(Scroller.ScrollToEnd), System.Windows.Threading.DispatcherPriority.Background);
    }
}
