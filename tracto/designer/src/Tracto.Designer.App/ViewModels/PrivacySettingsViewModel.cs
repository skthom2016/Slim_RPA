namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Privacy;

public sealed class PrivacySettingsViewModel : INotifyPropertyChanged
{
    private readonly PrivacySettings _settings;

    public PrivacySettingsViewModel(PrivacySettings settings)
    {
        _settings = settings;
        ResetCommand = new RelayCommand(_ => { settings.Reset(); OnAll(); });
        settings.PropertyChanged += (_, _) => OnAll();
    }

    public ICommand ResetCommand { get; }

    public bool RedactPasswords
    {
        get => _settings.HasFlag(PrivacyPolicy.RedactPasswords);
        set
        {
            // DS-14 FRD constraint: RedactPasswords is always enabled.
            // Reject attempts to disable it; the property remains true.
            if (!value) return;
            SetFlag(PrivacyPolicy.RedactPasswords, true);
            OnPropertyChanged();
        }
    }

    public bool RedactEmail
    {
        get => _settings.HasFlag(PrivacyPolicy.RedactEmail);
        set { SetFlag(PrivacyPolicy.RedactEmail, value); OnPropertyChanged(); }
    }

    public bool RedactPhone
    {
        get => _settings.HasFlag(PrivacyPolicy.RedactPhone);
        set { SetFlag(PrivacyPolicy.RedactPhone, value); OnPropertyChanged(); }
    }

    public bool RedactSsn
    {
        get => _settings.HasFlag(PrivacyPolicy.RedactSsn);
        set { SetFlag(PrivacyPolicy.RedactSsn, value); OnPropertyChanged(); }
    }

    public bool AllowScreenshots
    {
        get => _settings.AllowScreenshots;
        set { _settings.AllowScreenshots = value; OnPropertyChanged(); }
    }

    public bool AllowNarrationExport
    {
        get => _settings.AllowNarrationExport;
        set { _settings.AllowNarrationExport = value; OnPropertyChanged(); }
    }

    private void SetFlag(PrivacyPolicy flag, bool on) =>
        _settings.Policy = on ? _settings.Policy | flag : _settings.Policy & ~flag;

    private void OnAll()
    {
        OnPropertyChanged(nameof(RedactPasswords));
        OnPropertyChanged(nameof(RedactEmail));
        OnPropertyChanged(nameof(RedactPhone));
        OnPropertyChanged(nameof(RedactSsn));
        OnPropertyChanged(nameof(AllowScreenshots));
        OnPropertyChanged(nameof(AllowNarrationExport));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
