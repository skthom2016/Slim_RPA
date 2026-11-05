namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Browser;

/// <summary>
/// Drives the "Sign in" button beside a browser segment's profile. It opens a browser on that
/// profile folder, leaves it open while the person signs in to whatever the script needs, and
/// reports when they close it.
/// </summary>
public sealed class ProfileSignInViewModel : INotifyPropertyChanged
{
    private readonly IBrowserProfileSignIn _signIn;
    private readonly BrowserAdapterViewModel _browser;
    private string _status = string.Empty;
    private bool _isOpen;

    public ProfileSignInViewModel(IBrowserProfileSignIn signIn, BrowserAdapterViewModel browser)
    {
        _signIn = signIn;
        _browser = browser;
        SignInCommand = new RelayCommand(
            p => _ = SignInAsync(p as string),
            p => !_isOpen && _signIn.IsValidName(p as string));
    }

    /// <summary>Takes the profile name as its parameter, so it reads the segment being edited.</summary>
    public ICommand SignInCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set { _isOpen = value; OnPropertyChanged(); ((RelayCommand)SignInCommand).RaiseCanExecuteChanged(); }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    private async Task SignInAsync(string? profileName)
    {
        if (_isOpen || !_signIn.IsValidName(profileName)) return;
        var name = profileName!.Trim();

        IsOpen = true;
        try
        {
            Status = $"Signing in to profile \"{name}\" in {_browser.SelectedBrowser}. " +
                     "Sign in to the sites this script needs, then close the browser window.";

            await _signIn.SignInAsync(_browser.SelectedBrowser, name, _browser.StartUrl).ConfigureAwait(true);

            Status = $"Profile \"{name}\" saved. Runs that name it start already signed in.";
        }
        catch (Exception ex)
        {
            Status = $"Sign-in failed: {ex.Message}";
        }
        finally
        {
            IsOpen = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
