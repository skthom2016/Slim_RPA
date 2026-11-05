namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Browser;
using Tracto.Designer.Core.Scripting;

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
        SignInCommand = new RelayCommand(_ => _ = SignInAsync(), _ => !_isOpen);
    }

    /// <summary>Signs in the one shared profile every signed-in segment uses.</summary>
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

    private async Task SignInAsync()
    {
        if (_isOpen) return;

        IsOpen = true;
        try
        {
            Status = $"A {_browser.SelectedBrowser} window is open. Sign in to the sites your scripts need, " +
                     "then close the window.";

            await _signIn.SignInAsync(_browser.SelectedBrowser, ScriptSegment.SharedProfile, _browser.StartUrl)
                .ConfigureAwait(true);

            Status = "Signed-in browser saved. Segments set to \"Signed-in browser\" will use it.";
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
