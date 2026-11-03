namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Browser;

/// <summary>
/// Browser recording settings and control. With <see cref="LaunchBrowser"/> on (the default)
/// Start opens the chosen browser at <see cref="StartUrl"/> and records it; off, the recorder
/// attaches to a browser already running with the DevTools port open.
/// </summary>
public sealed class BrowserAdapterViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly BrowserCaptureAdapter _adapter;
    private BrowserType _selectedBrowser = BrowserType.Chromium;
    private int _debugPort = 9222;
    private string _startUrl = "https://www.bing.com/";
    private bool _launchBrowser = true;
    private string _status = "Not connected";

    public BrowserAdapterViewModel(BrowserCaptureAdapter adapter)
    {
        _adapter = adapter;
        ConnectCommand    = new RelayCommand(async _ => await StartAsync(), _ => !_adapter.IsRunning);
        DisconnectCommand = new RelayCommand(async _ => await StopAsync(),  _ => _adapter.IsRunning);
    }

    public ICommand ConnectCommand    { get; }
    public ICommand DisconnectCommand { get; }
    public Array BrowserTypes { get; } = new[] { BrowserType.Chromium, BrowserType.Chrome, BrowserType.Edge };

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

    public string StartUrl
    {
        get => _startUrl;
        set { _startUrl = value; OnPropertyChanged(); }
    }

    public bool LaunchBrowser
    {
        get => _launchBrowser;
        set { _launchBrowser = value; OnPropertyChanged(); }
    }

    public bool IsConnected => _adapter.IsRunning;

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public async Task StartAsync()
    {
        try
        {
            Status = _launchBrowser ? $"Launching {_selectedBrowser}..." : "Connecting...";
            await _adapter.StartAsync(new BrowserCaptureOptions(
                _selectedBrowser, _debugPort, null, _startUrl, _launchBrowser));
            Status = $"Recording {_selectedBrowser} (port {_debugPort})";
        }
        catch (Exception ex)
        {
            Status = $"Failed: {ex.Message}";
        }
        Refresh();
    }

    public async Task StopAsync()
    {
        if (!_adapter.IsRunning) return;
        try
        {
            await _adapter.StopAsync();
            Status = "Disconnected";
        }
        catch (Exception ex)
        {
            Status = $"Stop failed: {ex.Message}";
        }
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsConnected));
        ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public void Dispose() => _adapter.Dispose();
}
