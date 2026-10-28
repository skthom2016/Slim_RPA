namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Browser;

public sealed class BrowserAdapterViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IBrowserCaptureEngine _engine;
    private BrowserType _selectedBrowser = BrowserType.Chrome;
    private int _debugPort = 9222;
    private string _status = "Not connected";

    public BrowserAdapterViewModel(IBrowserCaptureEngine engine)
    {
        _engine = engine;
        ConnectCommand    = new RelayCommand(async _ => await ConnectAsync(),    _ => !_engine.IsRunning);
        DisconnectCommand = new RelayCommand(async _ => await DisconnectAsync(), _ => _engine.IsRunning);
    }

    public ICommand ConnectCommand    { get; }
    public ICommand DisconnectCommand { get; }
    public Array BrowserTypes { get; } = Enum.GetValues<BrowserType>();

    public BrowserType SelectedBrowser
    {
        get => _selectedBrowser;
        set { _selectedBrowser = value; OnPropertyChanged(); }
    }

    public int DebugPort
    {
        get => _debugPort;
        set { _debugPort = value; OnPropertyChanged(); }
    }

    public bool IsConnected => _engine.IsRunning;

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    private async Task ConnectAsync()
    {
        try
        {
            Status = "Connecting...";
            await _engine.StartAsync(new BrowserCaptureOptions(_selectedBrowser, _debugPort));
            Status = $"Connected ({_selectedBrowser}:{_debugPort})";
        }
        catch (Exception ex)
        {
            Status = $"Failed: {ex.Message}";
        }
        OnPropertyChanged(nameof(IsConnected));
        ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
    }

    private async Task DisconnectAsync()
    {
        await _engine.StopAsync();
        Status = "Disconnected";
        OnPropertyChanged(nameof(IsConnected));
        ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public void Dispose() => _engine.Dispose();
}
