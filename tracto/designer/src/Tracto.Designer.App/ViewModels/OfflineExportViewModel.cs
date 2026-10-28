namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.OfflineExport;

public sealed class OfflineExportViewModel : INotifyPropertyChanged
{
    private readonly IOfflineExporter _exporter;
    private readonly ScriptEditorViewModel _editor;
    private readonly SigningViewModel _signing;
    private bool _isIsolated, _includeScreenshots, _isExporting;
    private OfflineExportResult? _result;
    private CancellationTokenSource? _cts;

    public OfflineExportViewModel(IOfflineExporter exporter, ScriptEditorViewModel editor, SigningViewModel signing)
    {
        _exporter = exporter;
        _editor = editor;
        _signing = signing;
        ExportCommand = new RelayCommand(async _ => await ExportAsync(), _ => !_isExporting && _editor.Script is not null);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => _isExporting);
    }

    public ICommand ExportCommand { get; }
    public ICommand CancelCommand { get; }

    public bool IsIsolatedPackage
    {
        get => _isIsolated;
        set { _isIsolated = value; OnPropertyChanged(); }
    }

    public bool IncludeScreenshots
    {
        get => _includeScreenshots;
        set { _includeScreenshots = value; OnPropertyChanged(); }
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            _isExporting = value;
            OnPropertyChanged();
            ((RelayCommand)ExportCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
        }
    }

    public OfflineExportResult? LastResult
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

    private async Task ExportAsync()
    {
        if (_editor.Script is null) return;
        var s = _editor.Script;
        var outputPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Tracto", "Offline", $"{s.Id}_{s.Version}_offline.zip");
        var req = new OfflineExportRequest(s, _signing.CurrentSignature, outputPath, _isIsolated, _includeScreenshots);
        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsExporting = true;
        try { LastResult = await _exporter.ExportAsync(req, cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* export was cancelled by the user */ }
        finally { _cts = null; IsExporting = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
