namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.IsolatedMode;

public sealed class IsolatedModeViewModel : INotifyPropertyChanged
{
    private readonly IsolatedModeSettings _settings;
    private readonly IsolatedModeValidator _validator;
    private readonly ScriptEditorViewModel _editor;
    private IsolatedValidationResult? _validationResult;

    public IsolatedModeViewModel(IsolatedModeSettings settings, IsolatedModeValidator validator, ScriptEditorViewModel editor)
    {
        _settings = settings;
        _validator = validator;
        _editor = editor;

        ValidateCommand = new RelayCommand(_ => RunValidation(), _ => _editor.Script is not null);

        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsolatedModeSettings.IsEnabled))
                OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(BannerVisible));
        };
    }

    public ICommand ValidateCommand { get; }

    public bool IsEnabled
    {
        get => _settings.IsEnabled;
        set { _settings.IsEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(BannerVisible)); }
    }

    public bool RestrictNetworkAccess
    {
        get => _settings.RestrictNetworkAccess;
        set { _settings.RestrictNetworkAccess = value; OnPropertyChanged(); }
    }

    public bool BannerVisible => _settings.IsEnabled;

    public IsolatedValidationResult? ValidationResult
    {
        get => _validationResult;
        private set { _validationResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasResult)); }
    }

    public bool HasResult => _validationResult is not null;

    private void RunValidation()
    {
        if (_editor.Script is null) return;
        ValidationResult = _validator.Validate(_editor.Script, _settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
