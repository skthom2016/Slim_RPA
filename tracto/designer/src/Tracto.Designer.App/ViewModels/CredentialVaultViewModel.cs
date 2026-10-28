namespace Tracto.Designer.App.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Credentials;

public sealed class CredentialVaultViewModel : INotifyPropertyChanged
{
    private readonly ICredentialVault _vault;
    private string _newName = string.Empty;
    private string _newValue = string.Empty;
    private CredentialEntry? _selectedEntry;

    public CredentialVaultViewModel(ICredentialVault vault)
    {
        _vault = vault;
        AddCommand = new RelayCommand(
            _ => AddCredential(),
            _ => !string.IsNullOrWhiteSpace(_newName) && !string.IsNullOrWhiteSpace(_newValue));
        RemoveCommand = new RelayCommand(
            _ => RemoveCredential(),
            _ => _selectedEntry is not null);
        RefreshEntries();
    }

    public ObservableCollection<CredentialEntry> Credentials { get; } = [];

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    public string NewName
    {
        get => _newName;
        set
        {
            _newName = value;
            OnPropertyChanged();
            ((RelayCommand)AddCommand).RaiseCanExecuteChanged();
        }
    }

    public string NewValue
    {
        get => _newValue;
        set
        {
            _newValue = value;
            OnPropertyChanged();
            ((RelayCommand)AddCommand).RaiseCanExecuteChanged();
        }
    }

    public CredentialEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            _selectedEntry = value;
            OnPropertyChanged();
            ((RelayCommand)RemoveCommand).RaiseCanExecuteChanged();
        }
    }

    private void AddCredential()
    {
        _vault.StoreCredential(_newName.Trim(), _newValue);
        NewName = string.Empty;
        NewValue = string.Empty;
        RefreshEntries();
    }

    private void RemoveCredential()
    {
        if (_selectedEntry is null) return;
        _vault.RemoveCredential(_selectedEntry.Name);
        SelectedEntry = null;
        RefreshEntries();
    }

    private void RefreshEntries()
    {
        Credentials.Clear();
        foreach (var e in _vault.ListCredentials())
            Credentials.Add(e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
