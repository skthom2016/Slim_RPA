namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Mcp;

public sealed class AiAuthoringViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMcpAuthoringServer _server;
    private string _status = "AI Bridge: Not started";

    public AiAuthoringViewModel(IMcpAuthoringServer server)
    {
        _server = server;
        StartBridgeCommand = new RelayCommand(async _ => await StartBridgeAsync(), _ => !_server.IsRunning);
        StopBridgeCommand  = new RelayCommand(async _ => await StopBridgeAsync(),  _ => _server.IsRunning);
    }

    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }
    public bool IsRunning => _server.IsRunning;
    public ICommand StartBridgeCommand { get; }
    public ICommand StopBridgeCommand  { get; }

    private async Task StartBridgeAsync()
    {
        await _server.StartAsync();
        Status = _server.IsRunning ? $"AI Bridge: Running ({_server.EndpointAddress})" : "AI Bridge: Failed to start";
        OnPropertyChanged(nameof(IsRunning));
        ((RelayCommand)StartBridgeCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopBridgeCommand).RaiseCanExecuteChanged();
    }

    private async Task StopBridgeAsync()
    {
        await _server.StopAsync();
        Status = "AI Bridge: Stopped";
        OnPropertyChanged(nameof(IsRunning));
        ((RelayCommand)StartBridgeCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopBridgeCommand).RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public void Dispose() => _server.Dispose();
}
