namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Win32;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Infrastructure.Serialization;

public sealed class ExportImportViewModel : INotifyPropertyChanged
{
    private readonly ScriptEditorViewModel _editor;
    private string _selectedFormat = ".json";
    private string? _lastMessage;

    public ExportImportViewModel(ScriptEditorViewModel editor)
    {
        _editor = editor;
        ExportCommand = new RelayCommand(_ => Export(), _ => _editor.Script is not null);
        ImportCommand = new RelayCommand(_ => Import());
    }

    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }

    public string SelectedFormat
    {
        get => _selectedFormat;
        set { _selectedFormat = value; OnPropertyChanged(); }
    }

    public string? LastMessage
    {
        get => _lastMessage;
        private set { _lastMessage = value; OnPropertyChanged(); }
    }

    private void Export()
    {
        if (_editor.Script is null) return;
        var dlg = new SaveFileDialog
        {
            Title      = "Export Script",
            Filter     = "JSON Script|*.json|YAML Script|*.yaml",
            FileName   = $"{_editor.Script.Id}_{_editor.Script.Version}{_selectedFormat}",
            DefaultExt = _selectedFormat.TrimStart('.')
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            IScriptSerializer ser = dlg.FileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                ? new YamlScriptSerializer()
                : new JsonScriptSerializer();
            File.WriteAllText(dlg.FileName, ser.Serialize(_editor.Script));
            LastMessage = $"Exported to {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { LastMessage = $"Export failed: {ex.Message}"; }
    }

    private void Import()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Import Script",
            Filter = "Script Files|*.json;*.yaml|JSON Script|*.json|YAML Script|*.yaml"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            IScriptSerializer ser = dlg.FileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                ? new YamlScriptSerializer()
                : new JsonScriptSerializer();
            var content  = File.ReadAllText(dlg.FileName);
            var imported = ser.Deserialize(content);
            _editor.Script = imported;
            LastMessage = $"Imported '{imported.Name}' ({imported.Steps.Count} steps)";
        }
        catch (Exception ex) { LastMessage = $"Import failed: {ex.Message}"; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
